using System.Linq.Expressions;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.Samples.SchemaDataSets;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.DataSet;

// The rewriting dataset is its origin in every respect but the provider of
// its tables: the same schema and tables, the same rows, and LINQ over a
// table runs on the origin's provider, given the origin's table.
public sealed class SchemaDataSetTests
{
    private readonly RefusingProvider _provider = new();

    private readonly IStoredSchemaDataSet _origin;

    private readonly PureQLPostgreSqlSchemaDataSet _dataset;

    public SchemaDataSetTests()
    {
        _origin = RefusingTable.Schema(new SchemaDataSetWithForeignKeys(), _provider);
        _dataset = new PureQLPostgreSqlSchemaDataSet(_origin);
    }

    [Fact]
    public void SchemaIsTheOrigins()
    {
        Assert.Same(_origin.Schema, _dataset.Schema);
    }

    [Fact]
    public void TablesAreTheOrigins()
    {
        Assert.Equal(_origin.Count, _dataset.Count);
        Assert.Equal(_origin.Keys, _dataset.Keys);
        Assert.Equal(
            _origin.Values.Select(table => table.TableSchema),
            _dataset.Values.Select(table => table.TableSchema)
        );
        Assert.Equal(_origin.Keys, _dataset.Select(pair => pair.Key));
        Assert.Equal(
            _origin.Keys,
            _dataset
                .Cast<KeyValuePair<ITable, IStoredTableDataSet>>()
                .Select(pair => pair.Key)
        );
    }

    [Fact]
    public void TableIsFoundByItsKeyAsInTheOrigin()
    {
        ITable key = _origin.Keys.First();

        Assert.True(_dataset.ContainsKey(key));
        Assert.True(_dataset.TryGetValue(key, out IStoredTableDataSet? table));
        Assert.Equal<byte>(new TableHash(key), new TableHash(table.TableSchema));
        Assert.Equal<byte>(new TableHash(key), new TableHash(_dataset[key].TableSchema));
    }

    [Fact]
    public void TableTheOriginLacksIsMissingAsInTheOrigin()
    {
        ITable other = new AuditSchemaDataSet().Keys.First();

        Assert.False(_dataset.ContainsKey(other));
        Assert.False(_dataset.TryGetValue(other, out _));
        Assert.Equal(
            Record.Exception(() => _origin[other])?.GetType(),
            Record.Exception(() => _dataset[other])?.GetType()
        );
    }

    [Fact]
    public void TableHasTheOriginsRows()
    {
        foreach (ITable key in _origin.Keys)
        {
            Assert.Equal(_origin[key].AsEnumerable(), _dataset[key].AsEnumerable());
            Assert.Equal(
                ((System.Collections.IEnumerable)_origin[key]).Cast<IRow>(),
                ((System.Collections.IEnumerable)_dataset[key]).Cast<IRow>()
            );
        }
    }

    [Fact]
    public async Task TableHasTheOriginsRowsAsynchronously()
    {
        ITable key = _origin.Keys.First();
        List<IRow> rows = [];

        await foreach (IRow row in _dataset[key])
        {
            rows.Add(row);
        }

        Assert.Equal(_origin[key].AsEnumerable(), rows);
    }

    [Fact]
    public void TableIsItsOwnConstantOfRows()
    {
        IStoredTableDataSet table = _dataset.Values.First();

        Assert.Equal(typeof(IRow), table.ElementType);
        Assert.Same(
            table,
            Assert.IsType<ConstantExpression>(table.Expression, exactMatch: false).Value
        );
        Assert.Equal(typeof(IQueryable<IRow>), table.Expression.Type);
    }

    [Fact]
    public void QueryOverATableGoesToTheOriginsProviderOverTheOriginsTable()
    {
        ITable key = _origin.Keys.First();

        _ = Assert.Throws<NotSupportedException>(() =>
            Queryable.Where(_dataset[key], row => row.Cells.Count > 0).ToList()
        );

        Assert.Same(_origin[key], Root(Assert.Single(_provider.Queries)));
    }

    [Fact]
    public void UntypedQueryOverATableGoesToTheOriginsProvider()
    {
        IStoredTableDataSet table = _dataset.Values.First();

        _ = Assert.Throws<NotSupportedException>(() =>
            table.Provider.CreateQuery(table.Expression)
        );

        Assert.Same(_origin[table.TableSchema], Root(Assert.Single(_provider.Queries)));
    }

    [Fact]
    public void ExecutedQueryOverATableGoesToTheOriginsProvider()
    {
        IStoredTableDataSet table = _dataset.Values.First();

        _ = Assert.Throws<NotSupportedException>(() => Queryable.Count(table));
        _ = Assert.Throws<NotSupportedException>(() =>
            table.Provider.Execute(
                Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Any),
                    [typeof(IRow)],
                    table.Expression
                )
            )
        );

        Assert.All(
            _provider.Queries,
            query => Assert.Same(_origin[table.TableSchema], Root(query))
        );
        Assert.Equal(2, _provider.Queries.Count);
    }

    [Fact]
    public void TablesHeldInMemoryAreTheOriginsOwn()
    {
        SchemaDataSetWithForeignKeys origin = new();
        PureQLPostgreSqlSchemaDataSet dataset = new(origin);

        Assert.All(
            dataset.Values,
            table =>
            {
                Assert.IsNotType<RewritingTable>(table);
                _ = Assert.IsType<EnumerableQuery<IRow>>(table.Provider);
            }
        );
    }

    [Fact]
    public void ProjectionRefusedByTheOriginRunsInMemory()
    {
        Sample sample = Sample.Named("UserNameColumnQuery");

        (Expression _, PureQLProjection projection) = RewrittenTree.Of(sample.Query);

        Assert.Equal(QueryExecution.InMemory, projection.Execution);
        _ = Assert.IsType<NotSupportedException>(projection.FallbackReason);
        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, sample.Query),
                false
            ),
            ResultText.Of(projection, false)
        );
    }

    // The table a query reads: the constant at the root of its tree.
    private static object? Root(Expression query)
    {
        while (query is MethodCallExpression call)
        {
            query = call.Arguments[0];
        }

        return ((ConstantExpression)query).Value;
    }
}
