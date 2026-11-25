using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// Aggregates over a range of the one sort key, of every type that has
// distances, ascending and descending, over keys that are null in some rows,
// at the ends of the calendar, translated with exactly the interpreter's
// results; and the ranges PostgreSQL cannot hold, in memory. Needs Docker.
public sealed class RangeTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    public static TheoryData<string, string, bool> Translated =>
        new()
        {
            {
                "n", /*lang=json,strict*/
                """{"preceding": 5}""",
                false
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": 5}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"following": 10}""",
                false
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": 1, "following": 1}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": "all"}""",
                false
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": "all"}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"following": "all"}""",
                true
            },
            {
                "n", /*lang=json,strict*/
                """{"preceding": 9223372036854775807}""",
                false
            },
            {
                "amount", /*lang=json,strict*/
                """{"preceding": {"type": {"name": "decimal"}, "value": "0.75"}}""",
                false
            },
            {
                "amount", /*lang=json,strict*/
                """{"preceding": 1, "following": {"type": {"name": "decimal"}, "value": "0.5"}}""",
                true
            },
            {
                "amount", /*lang=json,strict*/
                """{"following": "all"}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 1}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 31}}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"days": 365}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"months": 1}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"months": 1}, "following": {"months": 1}}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"following": "all"}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"following": {"days": 5000000}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": {"seconds": 3600}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": {"seconds": {"type": {"name": "decimal"}, "value": "0.0000005"}}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"following": {"seconds": 86400}}""",
                true
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": {"months": 2}}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"preceding": "all", "following": {"seconds": 1}}""",
                true
            },
        };

    [Theory]
    [MemberData(nameof(Translated))]
    public void RangeHoldsTheRowsWithinTheDistances(
        string key,
        string range,
        bool descending
    )
    {
        foreach (string aggregate in Aggregates(Over(key, range, descending)))
        {
            _ = Translation.Translated(_database, Valued(aggregate));
        }
    }

    // "All" towards smaller keys over a date or a datetime that may be null,
    // a distance PostgreSQL cannot subtract from every date, and a range in
    // days and in months: PostgreSQL has no frame for them.
    public static TheoryData<string, string, bool> InMemory =>
        new()
        {
            {
                "day", /*lang=json,strict*/
                """{"preceding": "all"}""",
                false
            },
            {
                "at", /*lang=json,strict*/
                """{"following": "all"}""",
                true
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 1}, "following": {"months": 1}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"days": 2000000}}""",
                false
            },
            {
                "day", /*lang=json,strict*/
                """{"preceding": {"months": 60000}}""",
                false
            },
        };

    [Theory]
    [MemberData(nameof(InMemory))]
    public void RangePostgreSqlCannotHoldRunsInMemory(
        string key,
        string range,
        bool descending
    )
    {
        _ = Translation.Refused(
            _database,
            Valued(Aggregates(Over(key, range, descending)).First())
        );
    }

    private static IEnumerable<string> Aggregates(string over)
    {
        yield return "{\"operator\": \"count\", \"type\": "
            + Type("integer")
            + ", \"over\": "
            + over
            + "}";
        yield return "{\"operator\": \"sum\", \"type\": "
            + Type("decimal")
            + ", \"selector\": "
            + Field("amount")
            + ", \"over\": "
            + over
            + "}";
        yield return "{\"operator\": \"min\", \"type\": "
            + Type("integer")
            + ", \"selector\": "
            + Field("id")
            + ", \"over\": "
            + over
            + "}";
        yield return "{\"operator\": \"max\", \"type\": "
            + Type("integer")
            + ", \"selector\": "
            + Field("id")
            + ", \"over\": "
            + over
            + "}";
    }

    private static string Over(string key, string range, bool descending)
    {
        string direction = descending ? ", \"direction\": \"desc\"" : "";

        return "{\"partitionBy\": ["
            + Field("grp")
            + "], \"orderBy\": [{\"expression\": "
            + Field(key)
            + direction
            + "}], \"range\": "
            + range
            + "}";
    }
}
