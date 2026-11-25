using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Samples;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The rewritten tree of every sample, handed to a provider that refuses it:
// it reads the origin's tables, holds a marker or a PureQL operator for each
// window and recursion, and the projection's result in memory is unchanged.
public sealed class SampleTreeTests
{
    public static TheoryData<string> Samples =>
        [
            .. Sample
                .All.Where(sample =>
                    !TranslatedSampleTests.ReadingNoStoredTable.Contains(sample.Name)
                )
                .Select(sample => sample.Name),
        ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void RewrittenTreeReadsTheOriginsTablesAndRunsInMemoryAsBefore(string name)
    {
        Sample sample = Sample.Named(name);
        (Expression tree, PureQLProjection projection) = RewrittenTree.Of(sample.Query);
        PureQLProjection memory = new(
            PostgreSqlDatabase.InMemory,
            sample.Query,
            SampleParameters.Values
        );
        string original = TreeText.Of(memory.Expression);
        string rewritten = TreeText.Of(tree);

        Assert.DoesNotContain(
            Nodes.Of(tree).OfType<ConstantExpression>(),
            constant => constant.Value is RewritingTable
        );
        Assert.Equal(
            Count(original, "PureQLWindows.Over("),
            Count(rewritten, "PureQLWindows.Over(")
                + Count(rewritten, "PostgreSqlWindows.Over(")
        );
        Assert.Equal(
            Count(original, "PureQLRecursion.Recursive("),
            Count(rewritten, "PureQLRecursion.Recursive(")
                + Count(rewritten, "PostgreSqlRecursion.WithRecursive(")
        );
        Assert.Equal(
            ResultText.Of(memory, sample.IsOrdered),
            ResultText.Of(projection, sample.IsOrdered)
        );
    }

    public static TheoryData<string, string> Markers =>
        new()
        {
            { "PartitionTotalsQuery", "PostgreSqlWindows.Over(" },
            {
                "RunningTotalsQuery",
                "[ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW]"
            },
            { "MovingAveragesQuery", "[ROWS BETWEEN 6 PRECEDING AND CURRENT ROW]" },
            {
                "RangeDistancesOverNumbersQuery",
                "[RANGE BETWEEN 5.5 PRECEDING AND 5.5 FOLLOWING]"
            },
            {
                "RangeDistancesOverDatesQuery",
                "[RANGE BETWEEN INTERVAL '86400 seconds' PRECEDING AND CURRENT ROW]"
            },
            { "RankingQuery", "PostgreSqlWindows.DenseRank(window)" },
            { "RankingQuery", "PostgreSqlWindows.CumeDist(window)" },
            { "RankingQuery", "PostgreSqlWindows.Ntile(window, 4)" },
            { "PreviousAndNextOrderQuery", "PostgreSqlWindows.Lag(window, " },
            { "PreviousAndNextOrderQuery", "PostgreSqlWindows.Lead(window, " },
            { "TopOrdersPerTenantQuery", "PostgreSqlWindows.RowNumber(window)" },
            { "AncestorsQuery", "PostgreSqlRecursion.WithRecursive(" },
            { "DescendantsQuery", "PostgreSqlRecursion.WithRecursive(" },
            { "GroupByMonthQuery", "new DateOnly(" },
        };

    [Theory]
    [MemberData(nameof(Markers))]
    public void SampleTreeHoldsTheMarker(string name, string marker)
    {
        Assert.Contains(
            marker,
            TreeText.Of(RewrittenTree.Of(Sample.Named(name).Query).Tree)
        );
    }

    // A window PostgreSQL computes otherwise stays a PureQL window.
    [Theory]
    [InlineData("MovingAveragesQuery")]
    [InlineData("EveryAggregateOverWindowsQuery")]
    [InlineData("TemporalMeansOverWindowsQuery")]
    public void SampleTreeKeepsAWindowAsItIs(string name)
    {
        Assert.Contains(
            "PureQLWindows.Over(",
            TreeText.Of(RewrittenTree.Of(Sample.Named(name).Query).Tree)
        );
    }

    private static int Count(string text, string part)
    {
        return text.Split(part, StringSplitOptions.None).Length - 1;
    }

    private sealed class Nodes : ExpressionVisitor
    {
        private readonly List<Expression> _nodes = [];

        public static IReadOnlyList<Expression> Of(Expression tree)
        {
            Nodes nodes = new();
            _ = nodes.Visit(tree);
            return nodes._nodes;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node is not null)
            {
                _nodes.Add(node);
            }

            return base.Visit(node);
        }
    }
}
