using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// Closed expressions are computed where the markers take a value.
public sealed class ClosedTests
{
    private static readonly long[] Items = [1L, 2L];

    [Fact]
    public void ExpressionReadingNoParameterIsClosed()
    {
        Expression<Func<long>> constant = () => 3L + 4L;
        Expression<Func<long, IEnumerable<long>>> lambda = n =>
            Items.Where(item => item > 1);

        Assert.True(Closed.Is(constant.Body));
        Assert.True(Closed.Is(lambda.Body));
        Assert.Equal(7L, Closed.Value<long>(constant.Body));
    }

    [Fact]
    public void ExpressionReadingAParameterIsNotClosed()
    {
        Expression<Func<long, IEnumerable<long>>> lambda = n =>
            Items.Where(item => item > n);

        Assert.False(Closed.Is(lambda.Body));
        Assert.False(Closed.Is(Expression.Parameter(typeof(long), "n")));
    }

    [Fact]
    public void ConstantIsItsValue()
    {
        Assert.Equal(5L, Closed.Value<long>(Expression.Constant(5L)));
    }
}
