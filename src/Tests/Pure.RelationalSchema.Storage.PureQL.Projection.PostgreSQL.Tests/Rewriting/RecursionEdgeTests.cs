using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Samples;
using PureQL.CSharp.Model;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The edges of rewriting a recursion: depths of every closed form and at the
// ends of a long, depths the interpreter refuses, depths that are not closed,
// recursions that hold windows, functions and other recursions, and trees the
// rewriter has already rewritten.
public sealed class RecursionEdgeTests
{
    private const string WithRecursive = "PostgreSqlRecursion.WithRecursive(";

    private const string Recursive = "PureQLRecursion.Recursive(";

    private static readonly long[] Items = [1L, 2L];

    [Fact]
    public void DepthConvertedFromAnIntIsItsValueAsALong()
    {
        IntHolder depth = new() { Value = 9 };
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Select(n => n + 1), depth.Value);

        Assert.Equal(9L, Depth(Rewriter.Rewritten(tree.Body)));
    }

    [Fact]
    public void DepthComputedFromConstantsIsItsValue()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Select(n => n + 1), (2L * 3L) + 1L);

        Assert.Equal(7L, Depth(Rewriter.Rewritten(tree.Body)));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(int.MaxValue + 1L)]
    [InlineData(long.MaxValue)]
    public void DepthAtTheEndsOfALongIsKeptExactly(long maxDepth)
    {
        Expression tree = Walk(Expression.Constant(maxDepth));

        Assert.Equal(maxDepth, Depth(Rewriter.Rewritten(tree)));
    }

    // The interpreter fails a recursion of a depth below 1 whatever its rows
    // (ArgumentOutOfRangeException); a negative depth is refused by the
    // translator, so only 0 could run in PostgreSQL.
    [Fact]
    public void NegativeDepthNeverReachesPostgreSql()
    {
        Expression tree = Walk(Expression.Constant(-1L));
        Expression rewritten = Rewriter.Rewritten(tree);

        Assert.True(
            TreeText.Of(rewritten) == TreeText.Of(tree) || Depth(rewritten) == -1L
        );
    }

    // Observed: WithRecursive(..., 0L); PostgreSQL then returns the seed when
    // the first level is empty. Expected: the recursion kept as it is, so it
    // fails in memory as the interpreter fails a maxDepth below 1 (a PureQL
    // model built in C# can hold 0: only the JSON reader rejects it).
    [Fact(
        Skip = "Bug: a maxDepth of 0 is rewritten into WITH RECURSIVE, which runs where the interpreter fails a depth below 1"
    )]
    public void DepthBelowOneIsKept()
    {
        Expression tree = Walk(Expression.Constant(0L));

        Assert.Equal(TreeText.Of(tree), TreeText.Of(Rewriter.Rewritten(tree)));
    }

    // Observed: InvalidOperationException (variable 'depth' referenced from
    // scope '', but it is not defined) from Closed.Value, thrown by the
    // provider instead of a NotSupportedException. Expected: the recursion
    // kept, as the translator takes only a constant depth.
    [Fact(
        Skip = "Bug: a depth that reads a lambda parameter makes the rewriter throw InvalidOperationException instead of keeping the recursion"
    )]
    public void DepthReadingARowIsKept()
    {
        Expression<Func<IQueryable<long>, IQueryable<IQueryable<long>>>> tree = seed =>
            seed.Select(depth =>
                seed.Recursive(level => level.Select(n => n + 1), depth)
            );

        Expression rewritten = Rewriter.Rewritten(tree.Body);

        Assert.Contains(Recursive, TreeText.Of(rewritten), StringComparison.Ordinal);
        Assert.DoesNotContain(
            WithRecursive,
            TreeText.Of(rewritten),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RecursionInsideTheSeedOfAnotherIsRewrittenToo()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(inner => inner.Select(n => n + 1), 2)
                .Recursive(level => level.Where(n => n < 3), 4);

        string text = TreeText.Of(Rewriter.Rewritten(tree.Body));

        Assert.Equal(2, Count(text, WithRecursive));
        Assert.Equal(0, Count(text, Recursive));
    }

    [Fact]
    public void RewrittenRecursionIsNotRewrittenAgain()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Where(n => n < 10).Select(n => n + 1), 12);
        Expression once = Rewriter.Rewritten(tree.Body);

        Assert.Equal(TreeText.Of(once), TreeText.Of(Rewriter.Rewritten(once)));
    }

    [Fact]
    public void RecursionOfAQueryOverASequenceInMemoryIsRewrittenAsAQuery()
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            Items
                .AsQueryable()
                .Recursive(level => level.Where(n => n < 3).Select(n => n + 1), 5);

        Assert.Equal(5L, Depth(Rewriter.Rewritten(tree.Body)));
    }

    [Fact]
    public void RecursionWithAParameterDepthReadsTheBoundValue()
    {
        Expression tree = RewrittenTree
            .Of(
                Walk( /*lang=json,strict*/
                    """{"param": "page_size", "type": {"name": "integer"}}"""
                )
            )
            .Tree;
        string text = TreeText.Of(tree);

        Assert.Contains(WithRecursive, text, StringComparison.Ordinal);
        Assert.Contains(", 2L)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Recursive, text, StringComparison.Ordinal);
    }

    [Fact]
    public void RecursionWithAWindowInItsSeedHasBothMarkers()
    {
        string seed = $$$"""
            {
              "from": {"entity": "t.readings"},
              "select": [
                {"alias": "depth", "expression": {{{Literal("integer", "0")}}}},
                {"alias": "id", "expression": {"operator": "rowNumber", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {{{Field(
                "id"
            )}}}, "direction": "desc"}]} } }
              ]
            }
            """;
        string text = TreeText.Of(RewrittenTree.Of(Walk("3", seed)).Tree);

        Assert.Contains(WithRecursive, text, StringComparison.Ordinal);
        Assert.Contains(
            "PostgreSqlWindows.RowNumber(window)",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("PureQLWindows.Over(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RecursionWithAFunctionInItsNextHasTheFunctionsShape()
    {
        string id =
            $$$"""{"operator": "modulo", "type": {"name": "integer"}, "left": {"operator": "add", "type": {"name": "integer"}, "values": [{{{Level("id")}}}, {{{Literal("integer", "7")}}}]}, "right": {{{Literal("integer", "5")}}}}""";
        string text = TreeText.Of(RewrittenTree.Of(Walk("3", next: Next(id))).Tree);

        Assert.Contains(WithRecursive, text, StringComparison.Ordinal);
        Assert.DoesNotContain("PureQLMath.", text, StringComparison.Ordinal);
        Assert.Contains(" % ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRecursionsOfOneQueryAreBothRewritten()
    {
        PureQLQuery query = Query(
            $$$"""
            {
              "subqueries": [
                {{{Subquery("first", "2")}}},
                {{{Subquery("second", "4")}}}
              ],
              "from": {"subquery": "first"},
              "joins": [
                {"kind": "inner", "subquery": "second", "on": {"operator": "equal", "type": {"name": "boolean"}, "left": {"source": "first", "field": "id", "type": {"name": "integer"}}, "right": {"source": "second", "field": "id", "type": {"name": "integer"}} } }
              ],
              "select": [
                {"alias": "a", "expression": {"source": "first", "field": "depth", "type": {"name": "integer"}} },
                {"alias": "b", "expression": {"source": "second", "field": "depth", "type": {"name": "integer"}} }
              ]
            }
            """
        );
        (Expression tree, PureQLProjection projection) = RewrittenTree.Of(query);
        string text = TreeText.Of(tree);

        Assert.Equal(2, Count(text, WithRecursive));
        Assert.Equal(0, Count(text, Recursive));
        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, query),
                false
            ),
            ResultText.Of(projection, false)
        );
    }

    public static TheoryData<string> Samples =>
        [
            .. Sample
                .All.Where(sample =>
                    !TranslatedSampleTests.ReadingNoStoredTable.Contains(sample.Name)
                )
                .Select(sample => sample.Name),
        ];

    // A dataset of a dataset rewrites the tree twice: the second pass finds
    // nothing left to rewrite.
    [Theory]
    [MemberData(nameof(Samples))]
    public void RewritingARewrittenSampleTreeChangesNothing(string name)
    {
        Expression once = RewrittenTree.Of(Sample.Named(name).Query).Tree;

        Assert.Equal(TreeText.Of(once), TreeText.Of(Rewriter.Rewritten(once)));
    }

    private static long Depth(Expression rewritten)
    {
        MethodCallExpression call = Assert.IsType<MethodCallExpression>(
            exactMatch: false,
            @object: rewritten
        );

        Assert.Equal(WithRecursive, TreeText.Of(call)[..WithRecursive.Length]);

        return Assert.IsType<long>(
            Assert.IsType<ConstantExpression>(call.Arguments[2]).Value
        );
    }

    // seed.Recursive(level => level.Select(n => n + 1), maxDepth) over a
    // query of longs.
    private static Expression Walk(Expression maxDepth)
    {
        Expression<Func<IQueryable<long>, IQueryable<long>>> tree = seed =>
            seed.Recursive(level => level.Select(n => n + 1), 1);
        MethodCallExpression call = (MethodCallExpression)tree.Body;

        return Expression.Call(
            call.Method,
            call.Arguments[0],
            call.Arguments[1],
            maxDepth
        );
    }

    private static int Count(string text, string part)
    {
        return text.Split(part).Length - 1;
    }

    private static string Level(string field)
    {
        return $$$"""{"source": "walk", "field": "{{{field}}}", "type": {"name": "integer"}}""";
    }

    private static string Next(string id)
    {
        return $$$"""
            {
              "from": {"subquery": "walk"},
              "where": {"operator": "lessThan", "type": {"name": "boolean"}, "left": {{{Level(
                "depth"
            )}}}, "right": {{{Literal("integer", "2")}}}},
              "select": [
                {"alias": "depth", "expression": {"operator": "add", "type": {"name": "integer"}, "values": [{{{Level(
                "depth"
            )}}}, {{{Literal("integer", "1")}}}]}},
                {"alias": "id", "expression": {{{id}}}}
              ]
            }
            """;
    }

    private static string Seed()
    {
        return $$$"""
            {
              "from": {"entity": "t.readings"},
              "where": {"operator": "lessThanOrEqual", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Literal("integer", "2")}}}},
              "select": [
                {"alias": "depth", "expression": {{{Literal("integer", "0")}}}},
                {"alias": "id", "expression": {{{Field("id")}}}}
              ]
            }
            """;
    }

    private static string Subquery(string name, string maxDepth)
    {
        return Subquery(
            name,
            maxDepth,
            Seed(),
            Next(Level("id")).Replace("\"walk\"", $"\"{name}\"", StringComparison.Ordinal)
        );
    }

    private static string Subquery(string name, string maxDepth, string seed, string next)
    {
        return $$$"""
            {
              "name": "{{{name}}}",
              "recursive": {"seed": {{{seed}}}, "next": {{{next}}}, "maxDepth": {{{maxDepth}}}}
            }
            """;
    }

    // The walk over the readings with the given depth, seed and next.
    private static PureQLQuery Walk(
        string maxDepth,
        string? seed = null,
        string? next = null
    )
    {
        return Query(
            $$$"""
            {
              "subqueries": [{{{Subquery(
                "walk",
                maxDepth,
                seed ?? Seed(),
                next ?? Next(Level("id"))
            )}}}],
              "from": {"subquery": "walk"},
              "select": [
                {"alias": "depth", "expression": {{{Level("depth")}}}},
                {"alias": "id", "expression": {{{Level("id")}}}}
              ]
            }
            """
        );
    }

    private sealed class IntHolder
    {
        public int Value { get; init; }
    }
}
