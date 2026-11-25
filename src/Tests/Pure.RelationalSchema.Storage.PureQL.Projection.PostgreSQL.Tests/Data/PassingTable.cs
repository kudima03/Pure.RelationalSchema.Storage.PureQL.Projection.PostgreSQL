using System.Collections;
using System.Linq.Expressions;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.Samples.SchemaDataSets;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// A stored table held in memory behind a provider of its own, which records
// every tree it is given and runs it with LINQ to Objects over the rows: a
// stand-in for a translating provider that runs every query.
internal sealed class PassingTable(
    IStoredTableDataSet rows,
    PassingTable.Recorder provider
) : IStoredTableDataSet
{
    private readonly IStoredTableDataSet _rows = rows;

    public ITable TableSchema => _rows.TableSchema;

    public Type ElementType => typeof(IRow);

    public Expression Expression => Expression.Constant(this, typeof(IQueryable<IRow>));

    public IQueryProvider Provider { get; } = provider;

    public static IStoredSchemaDataSet Schema(
        IStoredSchemaDataSet dataset,
        Recorder provider
    )
    {
        return new StoredSchemaDataSet(
            dataset.Schema,
            [.. dataset.Values.Select(table => new PassingTable(table, provider))]
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

    // Records each tree and runs it in memory, every PassingTable in it
    // replaced by its rows.
    internal sealed class Recorder : IQueryProvider
    {
        public List<Expression> Queries { get; } = [];

        public IQueryable CreateQuery(Expression expression)
        {
            return Rows.CreateQuery(Recorded(expression));
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return Rows.CreateQuery<TElement>(Recorded(expression));
        }

        public object? Execute(Expression expression)
        {
            return Rows.Execute(Recorded(expression));
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return Rows.Execute<TResult>(Recorded(expression));
        }

        private static IQueryProvider Rows => Array.Empty<IRow>().AsQueryable().Provider;

        private Expression Recorded(Expression expression)
        {
            Queries.Add(expression);
            return new InMemory().Visit(expression);
        }

        private sealed class InMemory : ExpressionVisitor
        {
            protected override Expression VisitConstant(ConstantExpression node)
            {
                return node.Value is PassingTable table
                    ? Expression.Constant(
                        table._rows.AsEnumerable().AsQueryable(),
                        node.Type
                    )
                    : node;
            }
        }
    }
}
