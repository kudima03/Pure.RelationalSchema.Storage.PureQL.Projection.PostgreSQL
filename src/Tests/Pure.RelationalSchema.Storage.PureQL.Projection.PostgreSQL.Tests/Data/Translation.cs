using PureQL.CSharp.Model;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// A query run over the fixtures stored in PostgreSQL through the rewriting
// dataset, and in memory over the same rows: the results are the same, cell
// by cell, in order, and the query ran as expected (translated, or refused and
// run in memory).
internal static class Translation
{
    public static IReadOnlyList<string> Translated(
        PostgreSqlDatabase database,
        PureQLQuery query
    )
    {
        return Ran(database, query, QueryExecution.Translated);
    }

    public static IReadOnlyList<string> Refused(
        PostgreSqlDatabase database,
        PureQLQuery query
    )
    {
        return Ran(database, query, QueryExecution.InMemory);
    }

    // The query fails in both, as the specification fails it.
    public static void Fails(PostgreSqlDatabase database, PureQLQuery query)
    {
        _ = Assert.ThrowsAny<Exception>(() =>
            new PureQLProjection(PostgreSqlDatabase.InMemory, query).ToList()
        );
        _ = Assert.ThrowsAny<Exception>(() =>
            new PureQLProjection(database.Rewriting, query).ToList()
        );
    }

    private static IReadOnlyList<string> Ran(
        PostgreSqlDatabase database,
        PureQLQuery query,
        QueryExecution execution
    )
    {
        PureQLProjection actual = new(database.Rewriting, query);
        IReadOnlyList<string> result = ResultText.Of(actual, true);

        Assert.Equal(
            ResultText.Of(new PureQLProjection(PostgreSqlDatabase.InMemory, query), true),
            result
        );
        Assert.Equal(execution, actual.Execution);

        if (execution == QueryExecution.InMemory)
        {
            _ = Assert.IsType<NotSupportedException>(actual.FallbackReason);
        }

        return result;
    }
}
