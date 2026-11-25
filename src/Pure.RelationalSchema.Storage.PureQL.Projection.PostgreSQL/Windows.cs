using System.Linq.Expressions;
using System.Reflection;
using Linq.Expressions.Translation.PostgreSql;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// A window function of a plain query, PureQLWindows.Over(rows, partitionBy,
// orderBy, peers, frame, window => (window.Row, value)), is PostgreSQL's
// window function over OVER (PARTITION BY ... ORDER BY ... frame),
// PostgreSqlWindows.Over(rows, partitionBy, orderBy, frame, (row, window) =>
// (row, value)), where PostgreSQL computes what the specification's Window
// evaluation defines:
// - the partition: rows equal on every key, nulls together, 1 and 1.0 one key;
// - the order: the window's sort keys in turn, nulls first ascending and last
//   descending, strings by code point (COLLATE "C"), and peers the rows equal
//   on every key (the interpreter's peer key is the tuple of the sort keys);
// - the frame (Frames): rows counted from the current row, or a range of the
//   one sort key, both bounds included and the current row's peers inside;
// - rowNumber, rank, denseRank and ntile (ROW_NUMBER, RANK, DENSE_RANK, NTILE);
//   percentRank and cumulativeDistribution from RANK, CUME_DIST and the rows
//   of the partition, divided exactly and rounded once to 28 places
//   (PostgreSQL's PERCENT_RANK and CUME_DIST themselves are double precision);
// - previous and next (LAG and LEAD with the offset and the default, which
//   PostgreSQL reads in the current row and only for a missing row);
// - count, sum, min, max, any and all over the frame (window aggregates, with
//   a predicate as FILTER), which the translator renders as over a group.
// The window is left as it is, and runs in memory, when its function is one
// PostgreSQL computes otherwise: average (AVG of numeric keeps about 20
// significant digits, not the specification's rounding to 28), and every
// aggregate of PureQLAggregates (statistics, temporal means), which the
// translator does not know; when its frame cannot be written for PostgreSQL
// (Frames); and when the columns of the rows it passes on would share their
// first 63 characters (ColumnNames). The translator refuses what it cannot
// translate (first, last and concatAll, which sort the frame, the min and max
// of strings and uuids), which then runs in memory too.
internal static class Windows
{
    private static readonly MethodInfo Over = typeof(PostgreSqlWindows).GetMethod(
        nameof(PostgreSqlWindows.Over)
    )!;

    public static Expression? Rewritten(MethodCallExpression call)
    {
        if (
            call.Method.DeclaringType != typeof(PureQLWindows)
            || call.Method.Name != nameof(PureQLWindows.Over)
            || !Recursion.IsQuery(call.Arguments[0].Type)
        )
        {
            return null;
        }

        Type element = call.Method.GetGenericArguments()[0];
        Type result = call.Method.GetGenericArguments()[3];
        LambdaExpression partitionBy = Lambda(call.Arguments[1]);
        LambdaExpression orderBy = Lambda(call.Arguments[2]);
        LambdaExpression selector = Lambda(call.Arguments[5]);
        bool ordered = orderBy.Body != orderBy.Parameters[0];
        Type sorted = typeof(IOrderedEnumerable<>).MakeGenericType(element);
        ParameterExpression row = Expression.Parameter(element, "row");
        ParameterExpression window = Expression.Parameter(
            typeof(PostgreSqlWindow<>).MakeGenericType(element),
            "window"
        );

        PureQLFrame pureQLFrame = Closed.Value<PureQLFrame>(call.Arguments[4]);
        bool partition = ordered && !pureQLFrame.IsRows && !pureQLFrame.IsRange;

        return
            !ColumnNames.AreDistinct(result)
            || (ordered && !sorted.IsAssignableFrom(orderBy.Body.Type))
            || !Frames
                .Of(pureQLFrame, ordered, Key(orderBy))
                .Written(out PostgreSqlFrame? frame)
            || WindowFunctions.Rewritten(selector, row, window, partition)
                is not Expression body
            ? null
            : (Expression)
                Expression.Call(
                    Over.MakeGenericMethod(element, result),
                    call.Arguments[0],
                    Partition(partitionBy, element),
                    Order(orderBy, element, ordered),
                    Expression.Constant(frame, typeof(PostgreSqlFrame)),
                    Expression.Quote(Expression.Lambda(body, row, window))
                );
    }

    // The type of the first sort key, the one key of a range.
    private static Type? Key(LambdaExpression orderBy)
    {
        Type? key = null;
        Expression body = orderBy.Body;

        while (body is MethodCallExpression { Arguments.Count: >= 2 } sort)
        {
            key = Lambda(sort.Arguments[1]).ReturnType;
            body = sort.Arguments[0];
        }

        return key;
    }

    private static LambdaExpression Lambda(Expression argument)
    {
        return argument as LambdaExpression
            ?? (LambdaExpression)((UnaryExpression)argument).Operand;
    }

    // The partition keys, a tuple of the row's values, as an object; none (the
    // empty tuple) is one partition of every row.
    private static Expression Partition(LambdaExpression partitionBy, Type element)
    {
        Type type = typeof(Expression<>).MakeGenericType(
            typeof(Func<,>).MakeGenericType(element, typeof(object))
        );

        return partitionBy.Body.Type == typeof(ValueTuple)
            ? Expression.Constant(null, type)
            : Expression.Quote(
                Expression.Lambda(
                    typeof(Func<,>).MakeGenericType(element, typeof(object)),
                    Expression.Convert(partitionBy.Body, typeof(object)),
                    partitionBy.Parameters
                )
            );
    }

    // The sorted rows of the partition, OrderBy of them followed by ThenBy, or
    // none for a window without an order.
    private static Expression Order(LambdaExpression orderBy, Type element, bool ordered)
    {
        Type rows = typeof(IEnumerable<>).MakeGenericType(element);
        Type sorted = typeof(IOrderedEnumerable<>).MakeGenericType(element);
        Type function = typeof(Func<,>).MakeGenericType(rows, sorted);

        return ordered
            ? Expression.Quote(
                Expression.Lambda(function, orderBy.Body, orderBy.Parameters)
            )
            : Expression.Constant(null, typeof(Expression<>).MakeGenericType(function));
    }
}
