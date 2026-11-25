using System.Linq.Expressions;
using Linq.Expressions.Translation.PostgreSql;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// PureQLRecursion.Recursive in, WITH RECURSIVE with the same depth limit out.
public sealed class RecursionTests
{
    [Fact]
    public void RecursionBecomesWithRecursiveAtTheSameDepth()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Where(n => n < 10).Select(n => n + 1), 12);

        MethodCallExpression rewritten = Assert.IsType<MethodCallExpression>(
            exactMatch: false,
            @object: Rewriter.Rewritten(tree.Body)
        );

        Assert.Equal(typeof(PostgreSqlRecursion), rewritten.Method.DeclaringType);
        Assert.Equal(3, rewritten.Arguments.Count);
        Assert.Equal(
            12L,
            Assert.IsType<ConstantExpression>(rewritten.Arguments[2]).Value
        );
        Assert.Equal(
            "PostgreSqlRecursion.WithRecursive(seed, level => level.Where(n => (n < 10L)).Select(n => (n + 1L)), 12L)",
            TreeText.Of(rewritten)
        );
    }

    [Fact]
    public void DepthOfAParameterIsItsValue()
    {
        Holder depth = new() { Value = 7 };
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Select(n => n + 1), depth.Value);

        MethodCallExpression rewritten = (MethodCallExpression)
            Rewriter.Rewritten(tree.Body);

        Assert.Equal(7L, Assert.IsType<ConstantExpression>(rewritten.Arguments[2]).Value);
    }

    [Fact]
    public void RecursionInsideTheStepOfAnotherIsRewrittenToo()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(
                level => level.Recursive(inner => inner.Select(n => n + 1), 2),
                3
            );

        Assert.DoesNotContain(
            "Recursive(",
            TreeText
                .Of(Rewriter.Rewritten(tree.Body))
                .Replace("WithRecursive(", "", StringComparison.Ordinal),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void QueryIsAGenericQueryable()
    {
        Assert.True(Recursion.IsQuery(typeof(IQueryable<long>)));
        Assert.False(Recursion.IsQuery(typeof(IQueryable)));
        Assert.False(Recursion.IsQuery(typeof(IEnumerable<long>)));
    }

    [Fact]
    public void RecursionOverAPlainSequenceIsKept()
    {
        Expression<Func<IEnumerable<long>, IEnumerable<long>>> tree = seed =>
            seed.Recursive(level => level.Select(n => n + 1), 3);

        Assert.Equal(TreeText.Of(tree.Body), TreeText.Of(Rewriter.Rewritten(tree.Body)));
    }

    private sealed class Holder
    {
        public long Value { get; init; }
    }
}
