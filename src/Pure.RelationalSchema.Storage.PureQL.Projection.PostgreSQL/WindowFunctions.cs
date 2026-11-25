using System.Linq.Expressions;
using System.Reflection;
using Linq.Expressions.Translation.PostgreSql;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The selector of a window, window => (window.Row, value) over a
// PureQLWindow, as the selector of PostgreSqlWindows.Over, (row, window) =>
// (row, value) over a PostgreSqlWindow: the current row is the row, the rows
// of the frame are the marker window's Rows (aggregated by LINQ, which the
// translator renders as window aggregates), RowNumber, Rank and DenseRank its
// functions of the same name, Ntile(buckets) NTILE, and previous / next,
// window.Has(shift) ? operand(window.At(shift)) : default(window.Row), LAG or
// LEAD of the operand by the offset with the default, PercentRank and
// CumulativeDistribution exact ratios over the whole partition. Anything
// PostgreSQL would compute otherwise than the specification leaves the
// selector as it is (null): those two over another frame, Average, and any
// other read of the window.
internal sealed class WindowFunctions : ExpressionVisitor
{
    private readonly ParameterExpression _origin;

    private readonly ParameterExpression _row;

    private readonly ParameterExpression _window;

    private readonly bool _partition;

    private bool _kept;

    private WindowFunctions(
        ParameterExpression origin,
        ParameterExpression row,
        ParameterExpression window,
        bool partition
    )
    {
        _origin = origin;
        _row = row;
        _window = window;
        _partition = partition;
    }

    // The selector over the marker's window, or null; partition says the
    // frame is the whole ordered partition, which percentRank and
    // cumulativeDistribution count the rows of.
    public static Expression? Rewritten(
        LambdaExpression selector,
        ParameterExpression row,
        ParameterExpression window,
        bool partition
    )
    {
        WindowFunctions functions = new(selector.Parameters[0], row, window, partition);
        Expression body = functions.Visit(selector.Body);
        return functions._kept ? null : body;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        _kept |= node == _origin;
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression != _origin)
        {
            return base.VisitMember(node);
        }

        switch (node.Member.Name)
        {
            case nameof(PureQLWindow<>.Row):
                return _row;
            case nameof(PureQLWindow<>.Rows):
                return Expression.Property(_window, nameof(PostgreSqlWindow<>.Rows));
            case nameof(PureQLWindow<>.RowNumber):
                return Function(nameof(PostgreSqlWindows.RowNumber));
            case nameof(PureQLWindow<>.Rank):
                return Function(nameof(PostgreSqlWindows.Rank));
            case nameof(PureQLWindow<>.DenseRank):
                return Function(nameof(PostgreSqlWindows.DenseRank));
            case nameof(PureQLWindow<>.PercentRank) when _partition:
                return Ratio(
                    Expression.Subtract(Function(nameof(PostgreSqlWindows.Rank)), One),
                    Expression.Subtract(Rows(), One)
                );
            case nameof(PureQLWindow<>.CumulativeDistribution) when _partition:
                return Ratio(Peers(), Rows());
            default:
                _kept = true;
                return node;
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (
            node.Method.DeclaringType == typeof(Enumerable)
            && node.Method.Name == nameof(Enumerable.Average)
        )
        {
            _kept = true;
            return node;
        }

        if (node.Object != _origin || node.Method.Name != nameof(PureQLWindow<>.Ntile))
        {
            return base.VisitMethodCall(node);
        }

        // NTILE takes an int: more buckets than that are more than the rows of
        // any partition, where every row is its own bucket in both.
        long buckets = Math.Min(Closed.Value<long>(node.Arguments[0]), int.MaxValue);

        return buckets < 1
            ? Kept(node)
            : Expression.Convert(
                Expression.Call(
                    Marker(nameof(PostgreSqlWindows.Ntile)),
                    _window,
                    Expression.Constant((int)buckets)
                ),
                typeof(long)
            );
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        if (
            node.Test
                is not MethodCallExpression
                {
                    Method.Name: nameof(PureQLWindow<>.Has)
                } has
            || has.Object != _origin
        )
        {
            return base.VisitConditional(node);
        }

        long shift = Closed.Value<long>(has.Arguments[0]);

        if (shift is 0 or < -int.MaxValue or > int.MaxValue)
        {
            return Kept(node);
        }

        ParameterExpression other = Expression.Parameter(_row.Type, "other");
        Expression? value = Shifted.Rewritten(node.IfTrue, _origin, shift, other);
        Expression fallback = Visit(node.IfFalse);

        return value is null
            ? Kept(node)
            : Expression.Call(
                Marker(
                        shift < 0
                            ? nameof(PostgreSqlWindows.Lag)
                            : nameof(PostgreSqlWindows.Lead)
                    )
                    .MakeGenericMethod(_row.Type, node.Type),
                _window,
                Expression.Lambda(value, other),
                Expression.Constant((int)Math.Abs(shift)),
                fallback
            );
    }

    private static ConstantExpression One { get; } = Expression.Constant(1L);

    // The number of rows of the frame, which is the partition.
    private Expression Rows()
    {
        return Expression.Call(
            typeof(Enumerable)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(Enumerable.LongCount)
                    && method.GetParameters().Length == 1
                )
                .MakeGenericMethod(_row.Type),
            Expression.Property(_window, nameof(PostgreSqlWindow<>.Rows))
        );
    }

    // The rows up to the row's last peer: CUME_DIST times the rows, a
    // double within far less than a half of that whole number of rows.
    private Expression Peers()
    {
        return Expression.Convert(
            Expression.Call(
                typeof(Math).GetMethod(nameof(Math.Round), [typeof(double)])!,
                Expression.Multiply(
                    Function(nameof(PostgreSqlWindows.CumeDist)),
                    Expression.Convert(Rows(), typeof(double))
                )
            ),
            typeof(long)
        );
    }

    // part / whole, two integers, exactly as the specification divides them:
    // 0 for no whole (percentRank in a partition of one row), otherwise
    // rounded half away from zero to 28 places, which a value from 0 to 1
    // has, in exact integer arithmetic: floor((2 x part x 10^28 + whole) /
    // (2 x whole)) / 10^28, the floor taken as the dividend less its
    // remainder, which numeric computes exactly, and a multiple divides
    // exactly; TRUNC keeps no places of that whole number, which numeric
    // division may write, so that the result has 28 and a decimal holds it.
    private static Expression Ratio(Expression part, Expression whole)
    {
        Expression dividend = Expression.Add(
            Expression.Multiply(
                Expression.Convert(part, typeof(decimal)),
                Expression.Constant(20_000_000_000_000_000_000_000_000_000m)
            ),
            Expression.Convert(whole, typeof(decimal))
        );
        Expression divisor = Expression.Multiply(
            Expression.Convert(whole, typeof(decimal)),
            Expression.Constant(2m)
        );

        return Expression.Condition(
            Expression.Equal(whole, Expression.Constant(0L)),
            Expression.Constant(0m),
            Expression.Multiply(
                Expression.Call(
                    typeof(Math).GetMethod(nameof(Math.Truncate), [typeof(decimal)])!,
                    Expression.Divide(
                        Expression.Subtract(
                            dividend,
                            Expression.Modulo(dividend, divisor)
                        ),
                        divisor
                    )
                ),
                Expression.Constant(0.0000000000000000000000000001m)
            )
        );
    }

    private Expression Kept(Expression node)
    {
        _kept = true;
        return node;
    }

    private MethodCallExpression Function(string name)
    {
        return Expression.Call(Marker(name).MakeGenericMethod(_row.Type), _window);
    }

    private MethodInfo Marker(string name)
    {
        MethodInfo method = typeof(PostgreSqlWindows).GetMethod(name)!;
        return name == nameof(PostgreSqlWindows.Ntile)
            ? method.MakeGenericMethod(_row.Type)
            : method;
    }

    // The operand of previous or next, read in the row the shift reaches
    // (window.At(shift)), as a lambda over that row; null when it reads the
    // window otherwise.
    private sealed class Shifted(
        ParameterExpression origin,
        long shift,
        ParameterExpression other
    ) : ExpressionVisitor
    {
        private bool _other;

        public static Expression? Rewritten(
            Expression operand,
            ParameterExpression origin,
            long shift,
            ParameterExpression other
        )
        {
            Shifted shifted = new(origin, shift, other);
            Expression body = shifted.Visit(operand);
            return shifted._other ? null : body;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            return
                node.Object == origin
                && node.Method.Name == nameof(PureQLWindow<>.At)
                && Closed.Value<long>(node.Arguments[0]) == shift
                ? other
                : base.VisitMethodCall(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _other |= node == origin;
            return node;
        }
    }
}
