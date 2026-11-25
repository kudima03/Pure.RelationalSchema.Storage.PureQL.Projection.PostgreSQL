using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.Abstractions;
using PureQL.CSharp.Model;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// The tree the rewriting dataset hands to the provider of its tables for a
// query over the sample fixtures, and the projection, which then ran in
// memory.
internal static class RewrittenTree
{
    public static (Expression Tree, PureQLProjection Projection) Of(PureQLQuery query)
    {
        RefusingProvider provider = new();
        IStoredSchemaDataSet[] datasets =
        [
            .. PostgreSqlDatabase.InMemory.Select(
                dataset => new PureQLPostgreSqlSchemaDataSet(
                    RefusingTable.Schema(dataset, provider)
                )
            ),
        ];
        PureQLProjection projection = new(datasets, query, SampleParameters.Values);

        _ = projection.ToList();

        return (Assert.Single(provider.Queries), projection);
    }
}
