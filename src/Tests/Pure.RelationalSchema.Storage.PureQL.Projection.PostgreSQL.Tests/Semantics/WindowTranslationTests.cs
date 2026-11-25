using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// Every window function, frame and range over the readings, translated, with
// exactly the interpreter's results in memory: partitions with a null key and
// of one row, ties, null sort keys first ascending and last descending, both
// ends of a partition. Needs Docker.
public sealed class WindowTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    // Windows whose order has no ties between rows, for the functions that
    // depend on the order of peers, and windows over tied keys, for those
    // that do not.
    public static TheoryData<string> Distinct =>
        [
            Over(true, ("amount", false), ("id", false)),
            Over(true, ("amount", true), ("id", true)),
            Over(false, ("day", false), ("id", false)),
            Over(false, ("label", false)),
            Over(true, ("n", true), ("id", false)),
            Over(false, ("uid", false)),
        ];

    public static TheoryData<string> Tied =>
        [
            Over(true, ("amount", false)),
            Over(true, ("amount", true)),
            Over(false, ("n", false)),
            Over(false, ("n", true)),
            Over(true, ("grp", false)),
            Over(false, ("day", false)),
            Over(true, ("clock", true)),
        ];

    [Theory]
    [MemberData(nameof(Distinct))]
    public void RowNumberIsThePositionInThePartition(string over)
    {
        _ = Translation.Translated(
            _database,
            Valued(Function("rowNumber", "integer", over))
        );
    }

    [Theory]
    [MemberData(nameof(Tied))]
    [MemberData(nameof(Distinct))]
    public void RankingIsByPeers(string over)
    {
        foreach (string function in new[] { "rank", "denseRank" })
        {
            _ = Translation.Translated(
                _database,
                Valued(Function(function, "integer", over))
            );
        }

        foreach (string function in new[] { "percentRank", "cumulativeDistribution" })
        {
            _ = Translation.Translated(
                _database,
                Valued(Function(function, "decimal", over))
            );
        }
    }

    [Theory]
    [MemberData(nameof(Distinct))]
    public void NtileFillsBucketsInOrder(string over)
    {
        foreach (int buckets in new[] { 1, 2, 3, 4, 10, 1000 })
        {
            _ = Translation.Translated(
                _database,
                Valued(
                    $$"""{"operator": "ntile", "type": {"name": "integer"}, "buckets": {{Literal("integer", buckets.ToString(System.Globalization.CultureInfo.InvariantCulture))}}, "over": {{over}}}"""
                )
            );
        }
    }

    public static TheoryData<string, string, string?> Neighbours =>
        new()
        {
            { "previous", "", null },
            { "next", "", null },
            {
                "previous",
                "\"offset\": {\"type\": {\"name\": \"integer\"}, \"value\": 2},",
                null
            },
            {
                "next",
                "\"offset\": {\"type\": {\"name\": \"integer\"}, \"value\": 3},",
                null
            },
            { "previous", "", Literal("decimal", "\"0.5\"") },
            {
                "next",
                "\"offset\": {\"type\": {\"name\": \"integer\"}, \"value\": 2},",
                Literal("integer", "-1")
            },
            {
                "previous",
                "",
                $$"""{"operator": "multiply", "type": {"name": "integer"}, "values": [{{Field("id")}}, {{Literal("integer", "10")}}]}"""
            },
        };

    [Theory]
    [MemberData(nameof(Neighbours))]
    public void NeighbourIsTheOperandInAnotherRowOrTheDefault(
        string function,
        string offset,
        string? fallback
    )
    {
        foreach (string over in Distinct)
        {
            string defaulted = fallback is null ? "" : $"\"default\": {fallback},";

            _ = Translation.Translated(
                _database,
                Valued(
                    $$"""{"operator": "{{function}}", "type": {{Type("decimal", true)}}, "operand": {{Field("amount")}}, {{offset}} {{defaulted}} "over": {{over}}}"""
                )
            );
        }
    }

    [Fact]
    public void NeighbourOfANonNullOperandWithADefaultIsNeverNull()
    {
        IReadOnlyList<string> rows = Translation.Translated(
            _database,
            Valued(
                $$"""{"operator": "previous", "type": {{Type("integer")}}, "operand": {{Field("id")}}, "default": {{Literal("integer", "0")}}, "over": {{Over(false, ("id", false))}}}"""
            )
        );

        Assert.Equal("1 | 0", rows[1]);
        Assert.Equal("10 | 9", rows[^1]);
    }

    public static TheoryData<string> Frames =>
        [
            /*lang=json,strict*/
            """{"preceding": 1}""",
            /*lang=json,strict*/
            """{"preceding": "all"}""",
            /*lang=json,strict*/
            """{"following": 2}""",
            /*lang=json,strict*/
            """{"following": "all"}""",
            /*lang=json,strict*/
            """{"preceding": 1, "following": 1}""",
            /*lang=json,strict*/
            """{"preceding": "all", "following": 1}""",
            /*lang=json,strict*/
            """{"preceding": 2, "following": "all"}""",
        ];

    [Theory]
    [MemberData(nameof(Frames))]
    public void AggregateOverAFrameOfRows(string frame)
    {
        string over = Over(true, ("day", false), ("id", false))
            .Replace("]}", $"], \"frame\": {frame}}}", StringComparison.Ordinal);

        foreach (string aggregate in Aggregates(over))
        {
            _ = Translation.Translated(_database, Valued(aggregate));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AggregateOverAPartition(bool partitioned)
    {
        string over = partitioned
            ? "{\"partitionBy\": [" + Field("grp") + "]}"
            : "{\"partitionBy\": [" + Field("label") + ", " + Field("n") + "]}";

        foreach (string aggregate in Aggregates(over))
        {
            _ = Translation.Translated(_database, Valued(aggregate));
        }
    }

    [Fact]
    public void AverageOverAWindowRunsInMemory()
    {
        _ = Translation.Refused(
            _database,
            Valued(
                "{\"operator\": \"average\", \"type\": "
                    + Type("decimal", true)
                    + ", \"selector\": "
                    + Field("amount")
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    [Fact]
    public void MedianOverAWindowRunsInMemory()
    {
        _ = Translation.Refused(
            _database,
            Valued(
                "{\"operator\": \"median\", \"type\": "
                    + Type("decimal", true)
                    + ", \"selector\": "
                    + Field("amount")
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    [Fact]
    public void FrameCountOfAParameterIsItsValue()
    {
        string over = Over(true, ("day", false), ("id", false))
            .Replace(
                "]}",
                """], "frame": {"preceding": {"param": "rows", "type": {"name": "integer"}}}}""",
                StringComparison.Ordinal
            );
        PureQLProjection translated = new(
            _database.Rewriting,
            Valued(Sum(over)),
            new Dictionary<string, Parameters.Abstractions.Parameter.IParameter>
            {
                ["rows"] = new Parameters.Parameter.IntegerParameter(
                    new Primitives.Number.Long(0)
                ),
            }
        );
        PureQLProjection memory = new(
            PostgreSqlDatabase.InMemory,
            Valued(Sum(over)),
            new Dictionary<string, Parameters.Abstractions.Parameter.IParameter>
            {
                ["rows"] = new Parameters.Parameter.IntegerParameter(
                    new Primitives.Number.Long(0)
                ),
            }
        );

        Assert.Equal(ResultText.Of(memory, true), ResultText.Of(translated, true));
        Assert.Equal(QueryExecution.Translated, translated.Execution);
    }

    [Fact]
    public void WindowsInEachOtherAndInOneSelectAreTranslated()
    {
        global::PureQL.CSharp.Model.PureQLQuery query = Query(
            $$"""
            {
              "from": {"entity": "t.readings"},
              "select": [
                {"alias": "id", "expression": {{Field("id")}}},
                {"alias": "rank", "expression": {{Function(
                "rank",
                "integer",
                Over(true, ("amount", false))
            )}}},
                {"alias": "running", "expression": {{Sum(
                Over(true, ("day", false), ("id", false)).Replace(
                    "]}",
                    "], \"frame\": {\"preceding\": \"all\"}}",
                    StringComparison.Ordinal
                )
            )}}},
                {"alias": "share", "expression": {{Difference()}}}
              ],
              "orderBy": [
                {"expression": {{Function(
                "rowNumber",
                "integer",
                Over(false, ("label", false))
            )}}, "direction": "desc"}
              ]
            }
            """
        );
        PureQLProjection translated = new(_database.Rewriting, query);
        PureQLProjection memory = new(PostgreSqlDatabase.InMemory, query);

        Assert.Equal(ResultText.Of(memory, true), ResultText.Of(translated, true));
        Assert.Equal(QueryExecution.Translated, translated.Execution);
    }

    private static string Difference()
    {
        return "{\"operator\": \"subtract\", \"type\": {\"name\": \"integer\"}, \"values\": ["
            + Function("rowNumber", "integer", Over(false, ("id", true)))
            + ", "
            + Function("denseRank", "integer", Over(false, ("grp", false)))
            + "]}";
    }

    private static IEnumerable<string> Aggregates(string over)
    {
        yield return Sum(over);
        yield return $$"""{"operator": "sum", "type": {{Type("integer")}}, "selector": {{Field("n")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "count", "type": {{Type("integer")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "count", "type": {{Type("integer")}}, "predicate": {"operator": "greaterThan", "type": {"name": "boolean"}, "left": {{Field("n")}}, "right": {{Literal("integer", "0")}}}, "over": {{over}}}""";
        yield return $$"""{"operator": "min", "type": {{Type("decimal", true)}}, "selector": {{Field("amount")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "max", "type": {{Type("date", true)}}, "selector": {{Field("day")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "min", "type": {{Type("time", true)}}, "selector": {{Field("clock")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "max", "type": {{Type("integer")}}, "selector": {{Field("id")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "any", "type": {{Type("boolean")}}, "predicate": {"operator": "lessThan", "type": {"name": "boolean"}, "left": {{Field("amount")}}, "right": {{Literal("integer", "2")}}}, "over": {{over}}}""";
        yield return $$"""{"operator": "all", "type": {{Type("boolean")}}, "predicate": {"operator": "greaterThan", "type": {"name": "boolean"}, "left": {{Field("id")}}, "right": {{Literal("integer", "2")}}}, "over": {{over}}}""";
    }

    private static string Sum(string over)
    {
        return $$"""{"operator": "sum", "type": {{Type("decimal")}}, "selector": {{Field("amount")}}, "over": {{over}}}""";
    }

    private static string Function(string name, string type, string over)
    {
        return $$"""{"operator": "{{name}}", "type": {{Type(type)}}, "over": {{over}}}""";
    }

    internal static string Over(
        bool partitioned,
        params (string Field, bool Descending)[] keys
    )
    {
        string partition = partitioned ? "\"partitionBy\": [" + Field("grp") + "], " : "";
        string order = string.Join(
            ", ",
            keys.Select(key =>
                key.Descending
                    ? $$"""{"expression": {{Field(key.Field)}}, "direction": "desc"}"""
                    : $$"""{"expression": {{Field(key.Field)}}}"""
            )
        );

        return "{" + partition + "\"orderBy\": [" + order + "]}";
    }
}
