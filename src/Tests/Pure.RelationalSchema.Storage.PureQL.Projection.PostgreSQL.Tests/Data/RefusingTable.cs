using System.Collections;
using System.Linq.Expressions;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.Samples.SchemaDataSets;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// A stored table held in memory whose provider records and refuses every
// query (RefusingProvider): a stand-in for a table of a translating provider,
// whose trees the tests read once the rewriting dataset hands them over.
internal sealed class RefusingTable(IStoredTableDataSet rows, RefusingProvider provider)
    : IStoredTableDataSet
{
    private readonly IStoredTableDataSet _rows = rows;

    public ITable TableSchema => _rows.TableSchema;

    public Type ElementType => typeof(IRow);

    public Expression Expression => Expression.Constant(this, typeof(IQueryable<IRow>));

    public IQueryProvider Provider { get; } = provider;

    // Every table of a schema dataset, refused by the provider.
    public static IStoredSchemaDataSet Schema(
        IStoredSchemaDataSet dataset,
        RefusingProvider provider
    )
    {
        return new StoredSchemaDataSet(
            dataset.Schema,
            [.. dataset.Values.Select(table => new RefusingTable(table, provider))]
        );
    }

    public IEnumerator<IRow> GetEnumerator()
    {
        return _rows.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IAsyncEnumerator<IRow> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        return _rows.GetAsyncEnumerator(cancellationToken);
    }
}
