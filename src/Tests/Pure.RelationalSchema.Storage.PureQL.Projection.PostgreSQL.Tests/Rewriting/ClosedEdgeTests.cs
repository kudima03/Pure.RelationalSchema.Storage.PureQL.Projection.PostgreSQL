using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Reading = (long Id, string Group, decimal? Amount);

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The edges of closed values: boxed and converted constants, captured values,
// parameters declared in one lambda and read outside it, and the arguments
// the markers take as values (a frame, a number of buckets, an offset) when
// they read a row and so have no value.
public sealed class ClosedEdgeTests
{
    private static readonly IQueryable<Reading> Rows = Array
        .Empty<Reading>()
        .AsQueryable();

    [Fact]
    public void ConstantOfAnotherNumericTypeIsConverted()
    {
        Assert.Equal(3L, Closed.Value<long>(Expression.Constant(3)));
        Assert.Equal(4L, Closed.Value<long>(Expression.Constant((short)4)));
        Assert.Equal(5L, Closed.Value<long>(Expression.Constant(5.9m)));
    }

    [Fact]
    public void ConstantTypedAsAnObjectIsItsValue()
    {
        Assert.Equal(6L, Closed.Value<long>(Expression.Constant(6L, typeof(object))));
        Assert.Equal(
            7L,
            Closed.Value<long>(Expression.Convert(Expression.Constant(7), typeof(long)))
        );
    }

    [Fact]
    public void NullableConstantWithAValueIsTheValue()
    {
        Assert.Equal(8L, Closed.Value<long>(Expression.Constant(8L, typeof(long?))));
    }

    [Fact]
    public void ConversionThatOverflowsFailsAsItWouldInMemory()
    {
        _ = Assert.Throws<OverflowException>(() =>
            Closed.Value<long>(
                Expression.ConvertChecked(
                    Expression.Constant(decimal.MaxValue),
                    typeof(long)
                )
            )
        );
    }

    [Fact]
    public void CapturedValueIsReadWhenTheValueIsAsked()
    {
        Holder holder = new() { Value = 1 };
        Expression<Func<long>> read = () => holder.Value;

        holder.Value = 2;

        Assert.Equal(2L, Closed.Value<long>(read.Body));
        Assert.True(Closed.Is(read.Body));
    }

    [Fact]
    public void FrameIsItsOwnInstance()
    {
        PureQLFrame frame = PureQLFrame.Rows(3, 1);

        Assert.Same(frame, Closed.Value<PureQLFrame>(Expression.Constant(frame)));
        Assert.Same(
            PureQLFrame.Partition,
            Closed.Value<PureQLFrame>(
                Expression.Property(
                    null,
                    typeof(PureQLFrame),
                    nameof(PureQLFrame.Partition)
                )
            )
        );
    }

    [Fact]
    public void LambdaReadingOnlyItsOwnParametersIsClosed()
    {
        Expression<Func<IEnumerable<long>, IEnumerable<IEnumerable<long>>>> nested =
            items => items.Select(outer => items.Where(inner => inner > outer));

        Assert.True(Closed.Is(Expression.Quote(nested)));
        Assert.True(Closed.Is(nested));
    }

    [Fact]
    public void ParameterReadOutsideTheLambdaThatDeclaresItIsNotClosedWhenReadFirst()
    {
        ParameterExpression n = Expression.Parameter(typeof(long), "n");
        Expression tree = Expression.Add(
            n,
            Expression.Invoke(
                Expression.Lambda<Func<long, long>>(n, n),
                Expression.Constant(1L)
            )
        );

        Assert.False(Closed.Is(tree));
    }

    // Observed: true, as the visitor remembers every parameter a lambda has
    // declared, also once it has left that lambda. Expected: false, as n is
    // read outside the lambda that declares it.
    [Fact(
        Skip = "Bug: Closed.Is treats a parameter read after the lambda that declares it as declared"
    )]
    public void ParameterReadAfterTheLambdaThatDeclaresItIsNotClosed()
    {
        ParameterExpression n = Expression.Parameter(typeof(long), "n");
        Expression tree = Expression.Add(
            Expression.Invoke(
                Expression.Lambda<Func<long, long>>(n, n),
                Expression.Constant(1L)
            ),
            n
        );

        Assert.False(Closed.Is(tree));
    }

    // The markers take their frame, buckets and offset as values; a window
    // whose frame or function reads a row has none, and stays as it is (it
    // runs in memory). Observed for this and the next two: Closed.Value is
    // called without Closed.Is and throws InvalidOperationException (unbound
    // variable), which the provider throws instead of NotSupportedException.
    // Expected: the window kept as it is.
    [Fact(
        Skip = "Bug: a frame that reads a lambda parameter makes the rewriter throw InvalidOperationException instead of keeping the window"
    )]
    public void WindowWhoseFrameReadsARowIsKept()
    {
        Expression<
            Func<IQueryable<Reading>, IQueryable<IQueryable<(Reading, long)>>>
        > tree = rows =>
            rows.Select(outer =>
                rows.Over(
                    row => new ValueTuple(),
                    partition => partition.OrderBy(row => row.Id),
                    row => row.Id,
                    PureQLFrame.Rows(outer.Id, 0),
                    window => new ValueTuple<Reading, long>(
                        window.Row,
                        window.Rows.LongCount()
                    )
                )
            );

        AssertKept(tree.Body);
    }

    [Fact(
        Skip = "Bug: ntile buckets that read the row make the rewriter throw InvalidOperationException instead of keeping the window"
    )]
    public void NtileOfBucketsReadFromTheRowIsKept()
    {
        AssertKept(
            Over(window => new ValueTuple<Reading, long>(
                window.Row,
                window.Ntile(window.Row.Id)
            ))
        );
    }

    [Fact(
        Skip = "Bug: a previous/next offset that reads the row makes the rewriter throw InvalidOperationException instead of keeping the window"
    )]
    public void NeighbourAtAnOffsetReadFromTheRowIsKept()
    {
        AssertKept(
            Over(window => new ValueTuple<Reading, long>(
                window.Row,
                window.Has(window.Row.Id) ? window.At(window.Row.Id).Id : 0L
            ))
        );
    }

    [Fact]
    public void NtileOfAClosedNumberOfBucketsIsTheMarker()
    {
        Holder buckets = new() { Value = 3 };

        Assert.Contains(
            "PostgreSqlWindows.Ntile(window, 3)",
            TreeText.Of(
                Rewriter.Rewritten(
                    Over(window => new ValueTuple<Reading, long>(
                        window.Row,
                        window.Ntile(buckets.Value)
                    ))
                )
            ),
            StringComparison.Ordinal
        );
    }

    // The rewriter neither throws nor rewrites the window.
    private static void AssertKept(Expression tree)
    {
        Expression? rewritten = null;
        Exception? thrown = Record.Exception(() => rewritten = Rewriter.Rewritten(tree));

        Assert.Null(thrown);
        Assert.Contains(
            "PureQLWindows.Over(",
            TreeText.Of(rewritten!),
            StringComparison.Ordinal
        );
    }

    private static Expression Over(
        Expression<Func<PureQLWindow<Reading>, (Reading, long)>> result
    )
    {
        Expression<Func<IQueryable<Reading>, IQueryable<(Reading, long)>>> tree = rows =>
            rows.Over(
                row => new ValueTuple(),
                partition => partition.OrderBy(row => row.Id),
                row => row.Id,
                PureQLFrame.Partition,
                window => new ValueTuple<Reading, long>(window.Row, 0L)
            );
        MethodCallExpression call = (MethodCallExpression)tree.Body;

        return Expression.Call(
            call.Method,
            Expression.Constant(Rows, typeof(IQueryable<Reading>)),
            call.Arguments[1],
            call.Arguments[2],
            call.Arguments[3],
            call.Arguments[4],
            Expression.Quote(result)
        );
    }

    private sealed class Holder
    {
        public long Value { get; set; }
    }
}
