using System.Collections;
using System.Linq.Expressions;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// A stored table whose provider rewrites a tree before the origin's provider
// runs it (RewritingProvider). A projection's tree holds the table as a
// constant, which is how the projection finds the provider to give the tree
// to; the rewritten tree holds the origin's own expression in its place, so
// the origin's provider finds its tables. Enumerated, it is the origin. It is
// a class, not a record, as the text of a record would print its expression.
internal sealed class RewritingTable : IStoredTableDataSet
{
    private RewritingTable(IStoredTableDataSet origin)
    {
        Origin = origin;
    }

    public IStoredTableDataSet Origin { get; }

    public ITable TableSchema => Origin.TableSchema;

    public Type ElementType => typeof(IRow);

    public Expression Expression => Expression.Constant(this, typeof(IQueryable<IRow>));

    public IQueryProvider Provider => new RewritingProvider(Origin.Provider);

    // The table a schema dataset holds for an origin's table: the origin
    // itself when it is held in memory, where the projection runs the tree
    // as it is.
    public static IStoredTableDataSet Of(IStoredTableDataSet origin)
    {
        return origin.Provider is EnumerableQuery ? origin : new RewritingTable(origin);
    }

    public IEnumerator<IRow> GetEnumerator()
    {
        return Origin.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IAsyncEnumerator<IRow> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        return Origin.GetAsyncEnumerator(cancellationToken);
    }
}
