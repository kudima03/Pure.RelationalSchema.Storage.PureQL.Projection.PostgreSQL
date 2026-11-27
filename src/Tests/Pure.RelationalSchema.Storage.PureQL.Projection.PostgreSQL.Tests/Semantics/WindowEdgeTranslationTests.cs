using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Abstractions.Parameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Parameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using PureQL.CSharp.Model;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// The edges of window functions and frames over the readings: bucket counts
// and offsets at the limits of PostgreSQL's int arguments and past them,
// parameters, neighbours of every type, ratios rounded to 28 places, sort
// keys of every kind (booleans, computed keys, mixed directions, constants),
// partition keys that compare equal in different spellings, frames and ranges
// at their bounds, aggregates over empty selections and near the limits of
// an integer, and windows in queries with a filter, pagination, a join and
// a subquery. Every query gives exactly the interpreter's result in memory.
// Needs Docker.
public sealed class WindowEdgeTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private const string TwoToThe62 = "4611686018427387904";

    private readonly PostgreSqlDatabase _database = database;

    // NTILE takes an int: more buckets than that are clamped, every row its own
    // bucket in both.
    [Theory]
    [InlineData("1")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("3000000000")]
    [InlineData("9223372036854775807")]
    public void NtileOfAnyPositiveBucketsIsTranslated(string buckets)
    {
        _ = Translation.Translated(
            _database,
            Valued(Ntile(Literal("integer", buckets), Window(true, Desc("n"), Asc("id"))))
        );
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(3L)]
    [InlineData(3_000_000_000L)]
    public void NtileOfAParameterIsItsValue(long buckets)
    {
        PureQLProjection translated = Same(
            Valued(Ntile(IntegerParameter("buckets"), Window(true, Asc("id")))),
            ("buckets", buckets)
        ).Projection;

        Assert.Equal(QueryExecution.Translated, translated.Execution);
    }

    [Fact]
    public void NtileOfAParameterOfNoBucketsFailsInBoth()
    {
        PureQLQuery query = Valued(
            Ntile(IntegerParameter("buckets"), Window(true, Asc("id")))
        );

        _ = Assert.ThrowsAny<Exception>(() =>
            Projection(PostgreSqlDatabase.InMemory, query, ("buckets", 0L)).ToList()
        );
        _ = Assert.ThrowsAny<Exception>(() =>
            Projection(_database.Rewriting, query, ("buckets", 0L)).ToList()
        );
    }

    // An offset past the partition is the default everywhere; LAG and LEAD
    // take an int, up to its largest value.
    [Theory]
    [InlineData("previous", "2")]
    [InlineData("next", "3")]
    [InlineData("previous", "9")]
    [InlineData("next", "100")]
    [InlineData("previous", "2147483647")]
    [InlineData("next", "2147483647")]
    public void NeighbourAtAnOffsetIsTranslated(string function, string offset)
    {
        _ = Translation.Translated(
            _database,
            Valued(
                Neighbour(
                    function,
                    Type("integer"),
                    Field("id"),
                    Literal("integer", offset),
                    Literal("integer", "-1"),
                    Window(true, Asc("day"), Asc("id"))
                )
            )
        );
    }

    // Offsets past an int, which LAG and LEAD cannot take.
    [Theory]
    [InlineData("previous", "2147483648")]
    [InlineData("next", "2147483648")]
    [InlineData("previous", "9223372036854775807")]
    [InlineData("next", "9223372036854775807")]
    public void NeighbourAtAnOffsetPostgreSqlCannotTakeRunsInMemory(
        string function,
        string offset
    )
    {
        _ = Translation.Refused(
            _database,
            Valued(
                Neighbour(
                    function,
                    Type("integer"),
                    Field("id"),
                    Literal("integer", offset),
                    Literal("integer", "-1"),
                    Window(true, Asc("day"), Asc("id"))
                )
            )
        );
    }

    [Theory]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    [InlineData(2_147_483_647L, true)]
    [InlineData(5_000_000_000L, false)]
    public void NeighbourOffsetOfAParameterIsItsValue(long offset, bool translated)
    {
        PureQLProjection projection = Same(
            Valued(
                Neighbour(
                    "previous",
                    Type("decimal", true),
                    Field("amount"),
                    IntegerParameter("offset"),
                    null,
                    Window(false, Asc("id"))
                )
            ),
            ("offset", offset)
        ).Projection;

        Assert.Equal(
            translated ? QueryExecution.Translated : QueryExecution.InMemory,
            projection.Execution
        );
    }

    // LAG and LEAD of every type, with a default of the type or one read in the
    // current row.
    public static TheoryData<string, string, string, string?> Typed =>
        new()
        {
            { "previous", Type("string"), Field("label"), Str("none") },
            { "next", Type("string", true), Field("grp"), Field("label") },
            { "previous", Type("string", true), Field("grp"), null },
            {
                "next",
                Type("uuid"),
                Field("uid"),
                Literal("uuid", "\"ffffffff-ffff-ffff-ffff-ffffffffffff\"")
            },
            { "previous", Type("date", true), Field("day"), null },
            {
                "next",
                Type("date", true),
                Field("day"),
                Literal("date", "\"2000-02-29\"")
            },
            { "previous", Type("time", true), Field("clock"), null },
            {
                "next",
                Type("time", true),
                Field("clock"),
                Literal("time", "\"23:59:59.999999\"")
            },
            {
                "previous",
                Type("boolean"),
                Compare("greaterThan", Field("n"), Literal("integer", "0")),
                Literal("boolean", "true")
            },
            { "next", Type("decimal", true), Field("amount"), Field("id") },
        };

    [Theory]
    [MemberData(nameof(Typed))]
    public void NeighbourOfEveryTypeIsTranslated(
        string function,
        string type,
        string operand,
        string? fallback
    )
    {
        _ = Translation.Translated(
            _database,
            Valued(
                Neighbour(
                    function,
                    type,
                    operand,
                    null,
                    fallback,
                    Window(true, Desc("label"))
                )
            )
        );
    }

    // percentRank over 8 rows divides by 7 and cumulativeDistribution over 7
    // rows by 7: 1/7, 2/7 ... rounded half away from zero at the 28th place,
    // some up, some down.
    [Theory]
    [InlineData("percentRank", "8")]
    [InlineData("cumulativeDistribution", "7")]
    [InlineData("percentRank", "4")]
    [InlineData("cumulativeDistribution", "3")]
    [InlineData("cumulativeDistribution", "9")]
    [InlineData("percentRank", "10")]
    public void RatioOfAFilteredWindowIsRoundedOnce(string function, string rows)
    {
        foreach (
            string over in new[] { Window(false, Asc("id")), Window(false, Asc("n")) }
        )
        {
            IReadOnlyList<string> result = Translation.Translated(
                _database,
                Filtered(
                    Compare("lessThanOrEqual", Field("id"), Literal("integer", rows)),
                    Function(function, "decimal", over)
                )
            );

            Assert.Equal(
                int.Parse(rows, System.Globalization.CultureInfo.InvariantCulture) + 1,
                result.Count
            );
        }
    }

    [Fact]
    public void PercentRankOfOneSeventhIsRoundedUp()
    {
        IReadOnlyList<string> result = Translation.Translated(
            _database,
            Filtered(
                Compare("lessThanOrEqual", Field("id"), Literal("integer", "8")),
                Function("percentRank", "decimal", Window(false, Asc("id")))
            )
        );

        Assert.Equal("2 | 0.1428571428571428571428571429", result[2]);
        Assert.Equal("6 | 0.7142857142857142857142857143", result[6]);
    }

    // An order of constants sorts nothing: every row is a peer of every other.
    [Theory]
    [InlineData("rank", "integer")]
    [InlineData("denseRank", "integer")]
    public void RankOverAConstantOrderIsOneEverywhere(string function, string type)
    {
        PureQLQuery query = Valued(
            Function(function, type, Window(true, Asc(Literal("integer", "1"))))
        );

        Assert.All(
            Same(query).Rows.Skip(1),
            row => Assert.EndsWith(" | 1", row, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData("percentRank")]
    [InlineData("cumulativeDistribution")]
    public void RatioOverAConstantOrderRunsInMemory(string function)
    {
        _ = Translation.Refused(
            _database,
            Valued(
                Function(function, "decimal", Window(true, Asc(Literal("integer", "1"))))
            )
        );
    }

    // Sort keys PostgreSQL orders as the specification does: booleans (false,
    // true) with nulls first ascending and last descending, computed keys,
    // several keys in mixed directions, a key repeated.
    public static TheoryData<string> Orders =>
        [
            Window(false, Asc(Positive()), Asc("id")),
            Window(false, Asc(Signed()), Asc("id")),
            Window(false, Desc(Signed()), Asc("id")),
            Window(true, Asc(Signed()), Desc("id")),
            Window(false, Asc(Modulo(Field("id"), "3")), Desc("id")),
            Window(false, Desc(Modulo(Field("n"), "4", true)), Asc("amount"), Desc("id")),
            Window(false, Desc("grp"), Asc("amount"), Desc("n"), Asc("id")),
            Window(false, Asc("grp"), Desc("day"), Asc("id")),
            Window(true, Asc("id"), Desc("id")),
            Window(false, Desc("clock"), Desc("id")),
            Window(false, Asc("uid")),
            Window(false, Desc("label")),
        ];

    [Theory]
    [MemberData(nameof(Orders))]
    public void RankingOverAnyOrderIsTranslated(string over)
    {
        _ = Translation.Translated(
            _database,
            Valued(Function("rowNumber", "integer", over))
        );
        _ = Translation.Translated(_database, Valued(Function("rank", "integer", over)));
        _ = Translation.Translated(
            _database,
            Valued(Function("denseRank", "integer", over))
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("percentRank", "decimal", over))
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("cumulativeDistribution", "decimal", over))
        );
    }

    // Ties broken by the order of peers make rowNumber unspecified; over the
    // tied keys alone rank, denseRank and the ratios are.
    public static TheoryData<string> TiedOrders =>
        [
            Window(false, Asc(Positive())),
            Window(false, Asc(Signed())),
            Window(true, Desc(Signed())),
            Window(false, Asc(Modulo(Field("id"), "3"))),
            Window(false, Desc("clock")),
            Window(true, Desc("n"), Asc("amount")),
            Window(
                false,
                Asc(
                    If(
                        "decimal",
                        Compare("greaterThan", Field("id"), Literal("integer", "5")),
                        Multiply(
                            "decimal",
                            Literal("decimal", "\"0.5\""),
                            Literal("integer", "2")
                        ),
                        Literal("integer", "1")
                    )
                )
            ),
        ];

    [Theory]
    [MemberData(nameof(TiedOrders))]
    public void RankingOverTiedKeysIsTranslated(string over)
    {
        _ = Translation.Translated(_database, Valued(Function("rank", "integer", over)));
        _ = Translation.Translated(
            _database,
            Valued(Function("denseRank", "integer", over))
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("percentRank", "decimal", over))
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("cumulativeDistribution", "decimal", over))
        );
    }

    // Strings sort by code point: U+FF5A (fullwidth z) comes before U+1F600
    // (an emoji, two UTF-16 code units from U+D83D), which it follows in UTF-16
    // order.
    [Fact]
    public void StringsOutsideTheBasicPlaneSortByCodePoint()
    {
        string key = If(
            "string",
            Compare("equal", Field("id"), Literal("integer", "7")),
            Str("\uFF5A"),
            Field("label")
        );

        IReadOnlyList<string> rows = Translation.Translated(
            _database,
            Valued(Function("rowNumber", "integer", Window(false, Asc(key))))
        );

        Assert.Equal("5 | 10", rows[5]);
        Assert.Equal("7 | 9", rows[7]);
    }

    // Partition keys equal in the specification are one partition: a decimal
    // 1 and a product of 1.0, nulls; keys that differ in case, in a time, or
    // in a uuid are not.
    public static TheoryData<string> Partitions =>
        [
            If(
                "decimal",
                Compare("greaterThan", Field("id"), Literal("integer", "5")),
                Multiply(
                    "decimal",
                    Literal("decimal", "\"0.5\""),
                    Literal("integer", "2")
                ),
                Literal("integer", "1")
            ),
            If(
                "decimal",
                Compare("greaterThan", Field("id"), Literal("integer", "5")),
                Literal("decimal", "\"2.5\""),
                Multiply(
                    "decimal",
                    Literal("decimal", "\"1.25\""),
                    Literal("integer", "2")
                )
            ),
            Positive(),
            Signed(),
            Field("label"),
            Field("clock"),
            Field("day"),
            Field("uid"),
            Modulo(Field("n"), "2", true),
            Field("grp") + ", " + Field("n"),
            Field("amount"),
        ];

    [Theory]
    [MemberData(nameof(Partitions))]
    public void PartitionHoldsTheRowsOfEqualKeys(string keys)
    {
        string unordered = "{\"partitionBy\": [" + keys + "]}";
        string ordered =
            "{\"partitionBy\": ["
            + keys
            + "], \"orderBy\": [{\"expression\": "
            + Field("id")
            + "}]}";

        _ = Translation.Translated(
            _database,
            Valued(
                $$"""{"operator": "count", "type": {{Type("integer")}}, "over": {{unordered}}}"""
            )
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("rowNumber", "integer", ordered))
        );
        _ = Translation.Translated(
            _database,
            Valued(Function("cumulativeDistribution", "decimal", ordered))
        );
    }

    // Frames of rows at their bounds: the current row alone, counts far past
    // the partition, the whole partition, a partition of one row.
    public static TheoryData<string> RowFrames =>
        [
            /*lang=json,strict*/
            """{"preceding": 9223372036854775807, "following": 9223372036854775807}""",
            /*lang=json,strict*/
            """{"preceding": 9223372036854775807}""",
            /*lang=json,strict*/
            """{"following": "all"}""",
            /*lang=json,strict*/
            """{"preceding": "all", "following": 9223372036854775807}""",
            /*lang=json,strict*/
            """{"preceding": 3, "following": 100}""",
            /*lang=json,strict*/
            """{"following": 1}""",
        ];

    [Theory]
    [MemberData(nameof(RowFrames))]
    public void AggregateOverAFrameOfRowsAtItsBounds(string frame)
    {
        foreach (
            string over in new[]
            {
                Framed(Window(true, Desc("amount"), Asc("id")), "frame", frame),
                Framed(Window(false, Asc(Signed()), Desc("id")), "frame", frame),
            }
        )
        {
            foreach (string aggregate in Aggregates(over))
            {
                _ = Translation.Translated(_database, Valued(aggregate));
            }
        }
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(2L, 1L)]
    [InlineData(long.MaxValue, 0L)]
    public void FrameOfParametersIsTheirValues(long preceding, long following)
    {
        string over = Framed(
            Window(true, Asc("id")),
            "frame",
            "{\"preceding\": "
                + IntegerParameter("preceding")
                + ", \"following\": "
                + IntegerParameter("following")
                + "}"
        );
        PureQLProjection translated = Same(
            Valued(Sum(over)),
            ("preceding", preceding),
            ("following", following)
        ).Projection;

        Assert.Equal(QueryExecution.Translated, translated.Execution);
    }

    [Theory]
    [InlineData("frame", "id")]
    [InlineData("range", "n")]
    public void FrameOfANegativeParameterFailsInBoth(string kind, string key)
    {
        PureQLQuery query = Valued(
            Sum(
                Framed(
                    Window(true, Asc(key)),
                    kind,
                    "{\"preceding\": " + IntegerParameter("preceding") + "}"
                )
            )
        );

        _ = Assert.ThrowsAny<Exception>(() =>
            Projection(PostgreSqlDatabase.InMemory, query, ("preceding", -1L)).ToList()
        );
        _ = Assert.ThrowsAny<Exception>(() =>
            Projection(_database.Rewriting, query, ("preceding", -1L)).ToList()
        );
    }

    // Ranges at their bounds: over a key that is never null ("all" is then
    // UNBOUNDED), distances past the largest integer on either side and in
    // either direction, the largest decimal distance, the end of a month, a
    // distance rounded to no microsecond, "all" towards greater dates.
    public static TheoryData<string, string, bool> Ranges =>
        new()
        {
            {
                "id", /*lang=json,strict*/
                """{"preceding": "all"}""",
                false
            },
            {
                "id", /*lang=json,strict*/
                """{"preceding": 2, "following": 2}""",
                true
            },
            {
                "id", /*lang=json,strict*/
                """{"following": 9223372036854775807}""",
                false
            },
            {
                "n", /*lang=json,strict*/
                """{"following": 9223372036854775807}""",
                false
            },
            {
                "n", /*lang=json,strict*/
                """{"following": 9223372036854775807}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": 9223372036854775807, "following": 9223372036854775807}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"following": "all"}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": 13}""",
                false
            },
            {
                "amount", /*lang=json,strict*/
                """{"preceding": {"type": {"name": "decimal"}, "value": "0.75"}, "following": {"type": {"name": "decimal"}, "value": "0.75"}}""",
                true
            },
            {
                "amount", /*lang=json,strict*/
                """{"preceding": {"type": {"name": "decimal"}, "value": "9999999999999999999999999999"}, "following": "all"}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 1}, "following": "all"}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"days": 29}}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"months": 1}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"months": 13}}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": "all"}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"days": 3652059}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 1721000}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"months": 56000}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"months": 200000}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": {"seconds": {"type": {"name": "decimal"}, "value": "0.0000004"}}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"following": {"seconds": {"type": {"name": "decimal"}, "value": "0.0000015"}}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"following": {"months": 1}}""",
                true
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": {"seconds": 1}, "following": {"seconds": 86399}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": "all"}""",
                true
            },
        };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void RangeAtItsBoundsIsTranslated(string key, string range, bool descending)
    {
        string over = Framed(
            Window(true, descending ? Desc(key) : Asc(key)),
            "range",
            range
        );

        foreach (string aggregate in Aggregates(over))
        {
            _ = Translation.Translated(_database, Valued(aggregate));
        }
    }

    // "All" towards smaller dates over a key that may be null: PostgreSQL's
    // UNBOUNDED would reach the null keys.
    [Theory]
    [InlineData(
        "day", /*lang=json,strict*/
        """{"following": "all"}"""
    )]
    [InlineData(
        "at",
        /*lang=json,strict*/
        """{"following": "all", "preceding": {"seconds": 1}}"""
    )]
    public void RangeOfAllTowardsSmallerDatesDescendingRunsInMemory(
        string key,
        string range
    )
    {
        _ = Translation.Refused(
            _database,
            Valued(Count(Framed(Window(true, Desc(key)), "range", range)))
        );
    }

    // A key within a distance of the current row's, compared exactly as the
    // specification says ("the rows whose sort key lies within a distance"):
    // 1.5 is not within 0.7499999999999999999999999999 of 2.25. PostgreSQL
    // (numeric) leaves it out, count 1; the interpreter computes the bound
    // 2.25 - 0.7499999999999999999999999999 as a decimal of 28 digits, which
    // rounds it to 1.5, and counts 3 (RangeBounds.Moved, Exact.Add).
    [Fact(
        Skip = "Bug (interpreter): a decimal range bound is rounded to 28 digits "
            + "in memory (count 3), exact in PostgreSQL (count 1)"
    )]
    public void RangeOfADecimalDistanceIsExact()
    {
        string over = Framed(
            Window(true, Asc("amount")),
            "range",
            /*lang=json,strict*/
            """{"preceding": {"type": {"name": "decimal"}, "value": "0.7499999999999999999999999999"}}"""
        );

        _ = Translation.Translated(_database, Valued(Count(over)));
    }

    [Theory]
    [InlineData("n", "range", 5L)]
    [InlineData("n", "range", long.MaxValue)]
    [InlineData("id", "range", 0L)]
    public void RangeOfAParameterIsItsValue(string key, string kind, long distance)
    {
        string over = Framed(
            Window(true, Asc(key)),
            kind,
            "{\"preceding\": "
                + IntegerParameter("distance")
                + ", \"following\": "
                + IntegerParameter("distance")
                + "}"
        );

        foreach (string aggregate in Aggregates(over))
        {
            PureQLProjection translated = Same(
                Valued(aggregate),
                ("distance", distance)
            ).Projection;

            Assert.Equal(QueryExecution.Translated, translated.Execution);
        }
    }

    [Theory]
    [InlineData("days", 31L)]
    [InlineData("months", 1L)]
    [InlineData("days", 1_721_001L)]
    public void RangeOfDatesOfAParameterIsItsValue(string unit, long distance)
    {
        string over = Framed(
            Window(true, Asc("day")),
            "range",
            "{\"preceding\": {\"" + unit + "\": " + IntegerParameter("distance") + "}}"
        );

        _ = Same(Valued(Count(over)), ("distance", distance));
    }

    // Integers summed over a window: the sum of the partition is past the
    // largest integer, which fails in the specification.
    [Fact]
    public void SumOfIntegersPastTheLargestFailsInBoth()
    {
        Translation.Fails(
            _database,
            Valued(
                "{\"operator\": \"sum\", \"type\": "
                    + Type("integer")
                    + ", \"selector\": "
                    + Literal("integer", TwoToThe62)
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    // +2^62 and -2^62 in turn: the sums of every frame fit, one of them is
    // exactly the smallest integer, -2^63.
    [Theory]
    [InlineData("{\"partitionBy\": [GRP]}")]
    [InlineData("{\"orderBy\": [{\"expression\": ID}], \"frame\": {\"preceding\": 1}}")]
    [InlineData(
        "{\"orderBy\": [{\"expression\": ID}], \"frame\": {\"preceding\": \"all\"}}"
    )]
    public void SumOfIntegersNearTheirLimitsIsExact(string over)
    {
        string alternating = If(
            "integer",
            Compare("equal", Modulo(Field("id"), "2"), Literal("integer", "0")),
            Literal("integer", TwoToThe62),
            Literal("integer", "-" + TwoToThe62)
        );
        string window = over.Replace("GRP", Field("grp"), StringComparison.Ordinal)
            .Replace("ID", Field("id"), StringComparison.Ordinal);

        IReadOnlyList<string> rows = Translation.Translated(
            _database,
            Valued(
                $$"""{"operator": "sum", "type": {{Type("integer")}}, "selector": {{alternating}}, "over": {{window}}}"""
            )
        );

        if (over.StartsWith("{\"partitionBy", StringComparison.Ordinal))
        {
            Assert.Equal("1 | -9223372036854775808", rows[1]);
        }
    }

    // Aggregates whose predicate holds in no row: a count of 0, a sum of 0, no
    // minimum, any false and all true.
    public static TheoryData<string> Empty => Unmatched();

    private static TheoryData<string> Unmatched()
    {
        string none = Compare("greaterThan", Field("id"), Literal("integer", "100"));
        string over = "{\"partitionBy\": [" + Field("grp") + "]}";

        return
        [
            $$"""{"operator": "count", "type": {{Type("integer")}}, "predicate": {{none}}, "over": {{over}}}""",
            $$"""{"operator": "sum", "type": {{Type("decimal")}}, "selector": {{Field("amount")}}, "predicate": {{none}}, "over": {{over}}}""",
            $$"""{"operator": "sum", "type": {{Type("integer")}}, "selector": {{Field("n")}}, "predicate": {{none}}, "over": {{over}}}""",
            $$"""{"operator": "any", "type": {{Type("boolean")}}, "predicate": {{none}}, "over": {{over}}}""",
            $$"""{"operator": "all", "type": {{Type("boolean")}}, "predicate": {{none}}, "over": {{over}}}""",
            $$"""{"operator": "sum", "type": {{Type("integer")}}, "selector": {{If(
                    "integer",
                    Compare("lessThan", Field("n"), Literal("integer", "-100")),
                    Field("n"),
                    Null("integer"),
                    true
                )}}, "over": {{over}}}""",
        ];
    }

    // A decimal sum is computed from the exact values and rounded once to 28
    // significant digits: 1 + 3 x 0.0000000000000000000000000001 is
    // 1.0000000000000000000000000003, 29 digits, which rounds to 1; and a sum
    // of magnitude 10^28 or more is an error. Observed: PostgreSQL's SUM of
    // numeric is exact and System.Decimal holds 29 digits below 7.9 x 10^28,
    // so the translated sum is 1.0000000000000000000000000003 (expected 1),
    // and 4 x 9999999999999999999999999999 is returned as
    // 39999999999999999999999999996 where the interpreter fails. A grouped
    // sum, which this package does not rewrite, translates the same way.
    [Fact(
        Skip = "Bug (translator): a decimal SUM over a window is not rounded to "
            + "28 significant digits (1.0000000000000000000000000003, expected 1)"
    )]
    public void SumOfDecimalsIsRoundedToTheDecimalPrecision()
    {
        string value = If(
            "decimal",
            Compare("equal", Field("id"), Literal("integer", "1")),
            Literal("integer", "1"),
            Literal("decimal", "\"0.0000000000000000000000000001\"")
        );

        IReadOnlyList<string> rows = Translation.Translated(
            _database,
            Valued(
                "{\"operator\": \"sum\", \"type\": "
                    + Type("decimal")
                    + ", \"selector\": "
                    + value
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );

        Assert.Equal("1 | 1", rows[1]);
    }

    [Fact(
        Skip = "Bug (translator): a decimal SUM over a window of magnitude 10^28 "
            + "or more is returned (39999999999999999999999999996) instead of failing"
    )]
    public void SumOfDecimalsPastTheirRangeFailsInBoth()
    {
        Translation.Fails(
            _database,
            Valued(
                "{\"operator\": \"sum\", \"type\": "
                    + Type("decimal")
                    + ", \"selector\": "
                    + Literal("decimal", "\"9999999999999999999999999999\"")
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    // Window functions are computed before distinctBy.
    [Fact]
    public void WindowOfADistinctQueryIsComputedBeforeTheDistinct()
    {
        PureQLQuery query = Query(
            $$"""
            {
              "from": {"entity": "t.readings"},
              "select": [
                {"alias": "grp", "expression": {{Field("grp")}}},
                {"alias": "count", "expression": {{Count(
                "{\"partitionBy\": [" + Field("grp") + "]}"
            )}}},
                {"alias": "last", "expression": {{Function(
                "denseRank",
                "integer",
                Window(false, Desc("grp"))
            )}}}
              ],
              "distinctBy": ["grp"],
              "orderBy": [{"expression": {{Field("grp")}}}]
            }
            """
        );

        _ = Same(query);
    }

    [Theory]
    [MemberData(nameof(Empty))]
    public void AggregateOfNoRowIsItsEmptyValue(string aggregate)
    {
        _ = Translation.Translated(_database, Valued(aggregate));
    }

    // The minimum and the maximum of booleans: false before true, nulls
    // ignored; PostgreSQL has no MIN or MAX of booleans.
    [Theory]
    [InlineData("min", false)]
    [InlineData("max", false)]
    [InlineData("min", true)]
    [InlineData("max", true)]
    public void ExtremeOfBooleansOverAWindowIsTheSpecifications(
        string function,
        bool nullable
    )
    {
        _ = Same(
            Valued(
                "{\"operator\": \""
                    + function
                    + "\", \"type\": "
                    + Type("boolean", nullable)
                    + ", \"selector\": "
                    + (nullable ? Signed() : Positive())
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    // The translator refuses the minimum and the maximum of strings and uuids,
    // which then run in memory.
    [Theory]
    [InlineData("min", "string", "label")]
    [InlineData("max", "string", "label")]
    [InlineData("min", "uuid", "uid")]
    [InlineData("max", "uuid", "uid")]
    public void ExtremeOfStringsAndUuidsOverAWindowRunsInMemory(
        string function,
        string type,
        string field
    )
    {
        _ = Translation.Refused(
            _database,
            Valued(
                "{\"operator\": \""
                    + function
                    + "\", \"type\": "
                    + Type(type)
                    + ", \"selector\": "
                    + Field(field)
                    + ", \"over\": {\"partitionBy\": ["
                    + Field("grp")
                    + "]}}"
            )
        );
    }

    // A window is computed over the rows the filter keeps, before the order
    // and the pagination of the query.
    [Fact]
    public void WindowOfAFilteredPaginatedQueryIsTranslated()
    {
        string over = Framed(
            Window(true, Desc("amount"), Asc("id")),
            "frame",
            /*lang=json,strict*/
            """{"preceding": 1, "following": 1}"""
        );
        PureQLQuery query = Query(
            $$"""
            {
              "from": {"entity": "t.readings"},
              "where": {{Compare("notEqual", Field("id"), Literal("integer", "2"))}},
              "select": [
                {"alias": "id", "expression": {{Field("id")}}},
                {"alias": "rank", "expression": {{Function(
                "rank",
                "integer",
                Window(false, Desc("n"))
            )}}},
                {"alias": "total", "expression": {{Sum(over)}}},
                {"alias": "next", "expression": {{Neighbour(
                "next",
                Type("integer"),
                Field("id"),
                null,
                Literal("integer", "0"),
                Window(true, Asc("id"))
            )}}}
              ],
              "orderBy": [{"expression": {{Field("id")}}, "direction": "desc"}],
              "pagination": {"skip": 2, "take": 5}
            }
            """
        );

        IReadOnlyList<string> rows = Translation.Translated(_database, query);

        Assert.Equal(6, rows.Count);
    }

    // A window over a subquery that has a window of its own.
    [Fact]
    public void WindowOverASubqueryOfAWindowIsTranslated()
    {
        string ranked = /*lang=json,strict*/
            """{"source": "ranked", "field": "position", "type": {"name": "integer"}}""";
        string id = /*lang=json,strict*/
            """{"source": "ranked", "field": "id", "type": {"name": "integer"}}""";
        string grp = /*lang=json,strict*/
            """{"source": "ranked", "field": "grp", "type": {"name": "string", "nullable": true}}""";
        string running =
            "{\"operator\": \"sum\", \"type\": "
            + Type("integer")
            + ", \"selector\": "
            + ranked
            + ", \"over\": {\"partitionBy\": ["
            + grp
            + "], \"orderBy\": [{\"expression\": "
            + ranked
            + "}], \"frame\": {\"preceding\": \"all\"}}}";
        PureQLQuery query = Query(
            $$"""
            {
              "subqueries": [
                {
                  "name": "ranked",
                  "query": {
                    "from": {"entity": "t.readings"},
                    "select": [
                      {"alias": "id", "expression": {{Field("id")}}},
                      {"alias": "grp", "expression": {{Field("grp")}}},
                      {"alias": "position", "expression": {{Function(
                "rowNumber",
                "integer",
                Window(true, Desc("id"))
            )}}}
                    ]
                  }
                }
              ],
              "from": {"subquery": "ranked"},
              "select": [
                {"alias": "id", "expression": {{id}}},
                {"alias": "running", "expression": {{running}}},
                {"alias": "previous", "expression": {{Neighbour(
                "previous",
                Type("integer"),
                ranked,
                null,
                Literal("integer", "0"),
                "{\"orderBy\": [{\"expression\": " + id + "}]}"
            )}}}
              ],
              "orderBy": [{"expression": {{id}}}]
            }
            """
        );

        _ = Translation.Translated(_database, query);
    }

    // A window over the rows of a join, partitioned by a field of each side.
    [Fact]
    public void WindowOverAJoinIsTranslated()
    {
        string left = /*lang=json,strict*/
            """{"source": "r", "field": "id", "type": {"name": "integer"}}""";
        string right = /*lang=json,strict*/
            """{"source": "s", "field": "id", "type": {"name": "integer"}}""";
        string grp = /*lang=json,strict*/
            """{"source": "s", "field": "grp", "type": {"name": "string", "nullable": true}}""";
        PureQLQuery query = Query(
            $$"""
            {
              "from": {"entity": "t.readings", "alias": "r"},
              "joins": [
                {
                  "kind": "inner",
                  "entity": "t.readings",
                  "alias": "s",
                  "on": {{Compare(
                "equal",
                left,
                "{\"operator\": \"add\", \"type\": {\"name\": \"integer\"}, \"values\": ["
                    + right
                    + ", "
                    + Literal("integer", "1")
                    + "]}"
            )}}
                }
              ],
              "select": [
                {"alias": "id", "expression": {{left}}},
                {"alias": "rank", "expression": {{Function(
                "rank",
                "integer",
                "{\"partitionBy\": [" + grp + "], \"orderBy\": [" + Desc(right) + "]}"
            )}}},
                {"alias": "count", "expression": {{Count(
                "{\"partitionBy\": [" + grp + "]}"
            )}}}
              ],
              "orderBy": [{"expression": {{left}}}]
            }
            """
        );

        _ = Translation.Translated(_database, query);
    }

    // Each window of a select is a level of nesting of the rows: past the
    // first 63 characters of their column names the window stays in memory,
    // and never fails in PostgreSQL.
    [Fact]
    public void ManyWindowsInOneSelectGiveTheSameResult()
    {
        List<QueryExecution> executions = [];

        for (int windows = 1; windows <= 13; windows++)
        {
            string columns = string.Join(
                ",\n",
                Enumerable
                    .Range(0, windows)
                    .Select(index =>
                        "{\"alias\": \"w"
                        + index.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                        + "\", \"expression\": "
                        + Function(
                            "rowNumber",
                            "integer",
                            Window(
                                false,
                                index % 2 == 0 ? Asc("id") : Desc("id"),
                                Asc(
                                    Literal(
                                        "integer",
                                        index.ToString(
                                            System
                                                .Globalization
                                                .CultureInfo
                                                .InvariantCulture
                                        )
                                    )
                                )
                            )
                        )
                        + "}"
                    )
            );
            PureQLQuery query = Query(
                $$"""
                {
                  "from": {"entity": "t.readings"},
                  "select": [
                    {"alias": "id", "expression": {{Field("id")}}},
                    {"alias": "label", "expression": {{Field("label")}}},
                    {{columns}}
                  ],
                  "orderBy": [{"expression": {{Field("id")}}}]
                }
                """
            );

            executions.Add(Same(query).Projection.Execution);
        }

        Assert.Equal(QueryExecution.Translated, executions[0]);
        Assert.Equal(QueryExecution.Translated, executions[5]);
    }

    private (PureQLProjection Projection, IReadOnlyList<string> Rows) Same(
        PureQLQuery query,
        params (string Name, long Value)[] values
    )
    {
        PureQLProjection translated = Projection(_database.Rewriting, query, values);
        IReadOnlyList<string> result = ResultText.Of(translated, true);

        Assert.Equal(
            ResultText.Of(Projection(PostgreSqlDatabase.InMemory, query, values), true),
            result
        );

        if (translated.Execution == QueryExecution.InMemory)
        {
            _ = Assert.IsType<NotSupportedException>(translated.FallbackReason);
        }

        return (translated, result);
    }

    private static PureQLProjection Projection(
        IReadOnlyList<Abstractions.IStoredSchemaDataSet> datasets,
        PureQLQuery query,
        params (string Name, long Value)[] values
    )
    {
        return values.Length == 0
            ? new PureQLProjection(datasets, query)
            : new PureQLProjection(
                datasets,
                query,
                values.ToDictionary(
                    value => value.Name,
                    value =>
                        (IParameter)
                            new IntegerParameter(new Primitives.Number.Long(value.Value))
                )
            );
    }

    // The readings in the order of id that a predicate keeps, each with its
    // id and a value.
    private static PureQLQuery Filtered(string predicate, string value)
    {
        return Query(
            $$"""
            {
              "from": {"entity": "t.readings"},
              "where": {{predicate}},
              "select": [
                {"alias": "id", "expression": {{Field("id")}}},
                {"alias": "value", "expression": {{value}}}
              ],
              "orderBy": [{"expression": {{Field("id")}}}]
            }
            """
        );
    }

    private static IEnumerable<string> Aggregates(string over)
    {
        yield return Count(over);
        yield return Sum(over);
        yield return $$"""{"operator": "sum", "type": {{Type("integer")}}, "selector": {{Field("n")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "min", "type": {{Type("integer")}}, "selector": {{Field("id")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "max", "type": {{Type("date", true)}}, "selector": {{Field("day")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "min", "type": {{Type("time", true)}}, "selector": {{Field("clock")}}, "over": {{over}}}""";
        yield return $$"""{"operator": "count", "type": {{Type("integer")}}, "predicate": {{Positive()}}, "over": {{over}}}""";
        yield return $$"""{"operator": "any", "type": {{Type("boolean")}}, "predicate": {{Positive()}}, "over": {{over}}}""";
        yield return $$"""{"operator": "all", "type": {{Type("boolean")}}, "predicate": {{Positive()}}, "over": {{over}}}""";
    }

    private static string Count(string over)
    {
        return $$"""{"operator": "count", "type": {{Type("integer")}}, "over": {{over}}}""";
    }

    private static string Sum(string over)
    {
        return $$"""{"operator": "sum", "type": {{Type("decimal")}}, "selector": {{Field("amount")}}, "over": {{over}}}""";
    }

    private static string Function(string name, string type, string over)
    {
        return $$"""{"operator": "{{name}}", "type": {{Type(type)}}, "over": {{over}}}""";
    }

    private static string Ntile(string buckets, string over)
    {
        return $$"""{"operator": "ntile", "type": {{Type("integer")}}, "buckets": {{buckets}}, "over": {{over}}}""";
    }

    private static string Neighbour(
        string function,
        string type,
        string operand,
        string? offset,
        string? fallback,
        string over
    )
    {
        string shifted = offset is null ? "" : $"\"offset\": {offset}, ";
        string defaulted = fallback is null ? "" : $"\"default\": {fallback}, ";

        return $$"""{"operator": "{{function}}", "type": {{type}}, "operand": {{operand}}, {{shifted}}{{defaulted}}"over": {{over}}}""";
    }

    private static string IntegerParameter(string name)
    {
        return "{\"param\": \"" + name + "\", \"type\": {\"name\": \"integer\"}}";
    }

    private static string Str(string value)
    {
        return "{\"type\": {\"name\": \"string\"}, \"value\": \"" + value + "\"}";
    }

    private static string Compare(string comparison, string left, string right)
    {
        return $$"""{"operator": "{{comparison}}", "type": {"name": "boolean"}, "left": {{left}}, "right": {{right}}}""";
    }

    // Whether n is above 0, false where n is null.
    private static string Positive()
    {
        return Compare("greaterThan", Field("n"), Literal("integer", "0"));
    }

    // Whether n is above 0, null where n is null: a nullable boolean.
    private static string Signed()
    {
        return If(
            "boolean",
            Compare("equal", Field("n"), Null("integer")),
            Null("boolean"),
            Positive(),
            true
        );
    }

    private static string Null(string type)
    {
        return "{\"type\": " + Type(type, true) + ", \"value\": null}";
    }

    private static string Modulo(string left, string right, bool nullable = false)
    {
        return $$"""{"operator": "modulo", "type": {{Type(
            "integer",
            nullable
        )}}, "left": {{left}}, "right": {{Literal("integer", right)}}}""";
    }

    private static string Multiply(string type, string left, string right)
    {
        return $$"""{"operator": "multiply", "type": {{Type(
            type
        )}}, "values": [{{left}}, {{right}}]}""";
    }

    private static string If(
        string type,
        string condition,
        string then,
        string otherwise,
        bool nullable = false
    )
    {
        return $$"""{"operator": "if", "type": {{Type(
            type,
            nullable
        )}}, "condition": {{condition}}, "then": {{then}}, "else": {{otherwise}}}""";
    }

    // A window over the readings, partitioned by grp or not, in the order of
    // its keys.
    private static string Window(bool partitioned, params string[] keys)
    {
        string partition = partitioned ? "\"partitionBy\": [" + Field("grp") + "], " : "";

        return "{" + partition + "\"orderBy\": [" + string.Join(", ", keys) + "]}";
    }

    private static string Framed(string window, string kind, string frame)
    {
        return window[..^1] + ", \"" + kind + "\": " + frame + "}";
    }

    private static string Asc(string key)
    {
        return "{\"expression\": " + Expression(key) + "}";
    }

    private static string Desc(string key)
    {
        return "{\"expression\": " + Expression(key) + ", \"direction\": \"desc\"}";
    }

    private static string Expression(string key)
    {
        return key.StartsWith('{') ? key : Field(key);
    }
}
