using System.Reflection;
using Npgsql;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// The rewritten parts and periods of a stored datetime when the session's
// time zone is not UTC (a server whose TimeZone is another, or a connection
// that sets one): the specification reads a datetime in UTC, as the
// interpreter does, whatever the server. A class of its own, as it changes the
// session of its database. Needs Docker.
public sealed class TemporalEdgeTimeZoneTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    // With SET TimeZone = 'Pacific/Kiritimati' (UTC+14), quarter of
    // 2023-12-31T12:00:00Z is 1 translated and 4 in memory, dayOfWeek of
    // 2024-01-31T10:00:00Z 4 for 3, and startOfYear of 0001-01-01T00:00:00Z
    // fails in PostgreSQL ("date field value out of range: 0-01-01"). The
    // provider reads a stored datetime from its cell text as a timestamptz
    // (the interpreter parses it assuming UTC), whose parts PostgreSQL
    // extracts in the session's zone; year and hour, which this package does
    // not rewrite, are as wrong, so the cause is upstream, but the rewritten
    // shapes assume a UTC session too ("a datetime is read and built in UTC").
    [Fact(
        Skip = "Bug (upstream provider, inherited by the rewritten shapes): parts of a stored datetime follow the session's TimeZone instead of UTC"
    )]
    public void PartsOfAStoredDatetimeDoNotFollowTheSessionTimeZone()
    {
        SessionTimeZone("Pacific/Kiritimati");

        foreach (
            string function in new[]
            {
                "quarter",
                "dayOfWeek",
                "startOfYear",
                "startOfDay",
            }
        )
        {
            string type = function.StartsWith("startOf", StringComparison.Ordinal)
                ? "datetime"
                : "integer";

            _ = Translation.Translated(
                _database,
                Valued(
                    "{\"operator\": \""
                        + function
                        + "\", \"type\": "
                        + Type(type, true)
                        + ", \"operand\": "
                        + Field("at")
                        + "}"
                )
            );
        }
    }

    // The fixture's connection, which its datasets read through, set to the
    // zone; the fixture gives no other way to reach the session.
    private void SessionTimeZone(string zone)
    {
        _ = _database.Datasets;
        NpgsqlConnection connection = (NpgsqlConnection)
            typeof(PostgreSqlDatabase)
                .GetField("_connection", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_database)!;

        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SET TimeZone = '{zone}';";
        _ = command.ExecuteNonQuery();
    }
}
