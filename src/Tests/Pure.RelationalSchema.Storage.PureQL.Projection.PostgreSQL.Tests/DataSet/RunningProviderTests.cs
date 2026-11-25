using System.Collections;
using System.Linq.Expressions;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.Samples.SchemaDataSets;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.DataSet;

// Over an origin whose provider runs every query, LINQ over a table and a
// projection over the dataset are answered by that provider, with the
// origin's results, over the origin's tables.
public sealed class RunningProviderTests
{
    private readonly PassingTable.Recorder _provider = new();

    private readonly IStoredSchemaDataSet _origin;

    private readonly PureQLPostgreSqlSchemaDataSet _dataset;

    public RunningProviderTests()
    {
        _origin = PassingTable.Schema(new SchemaDataSetWithForeignKeys(), _provider);
        _dataset = new PureQLPostgreSqlSchemaDataSet(_origin);
    }

    [Fact]
    public void QueryOverATableHasTheOriginsResult()
    {
        IStoredTableDataSet table = _dataset.Values.First();

        Assert.Equal(
            _origin.Values.First().AsEnumerable().Count(row => row.Cells.Count > 0),
            Queryable.Where(table, row => row.Cells.Count > 0).ToList().Count
        );
        Assert.Equal(
            _origin.Values.First().AsEnumerable().Count(),
            Queryable.Count(table)
        );
        Assert.Equal(
            _origin.Values.First().AsEnumerable().Any(),
            table.Provider.Execute(
                Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Any),
                    [typeof(IRow)],
                    table.Expression
                )
            )
        );
        Assert.Equal(
            _origin.Values.First().AsEnumerable().Count(),
            ((IEnumerable)table.Provider.CreateQuery(table.Expression))
                .Cast<object>()
                .Count()
        );
        Assert.Equal(4, _provider.Queries.Count);
    }

    [Fact]
    public void DatasetEnumeratesAsTheOrigin()
    {
        List<object> pairs = [];
        IEnumerator enumerator = ((IEnumerable)_dataset).GetEnumerator();

        while (enumerator.MoveNext())
        {
            pairs.Add(enumerator.Current);
        }

        Assert.Equal(
            _origin.Keys,
            pairs
                .Cast<KeyValuePair<ITable, IStoredTableDataSet>>()
                .Select(pair => pair.Key)
        );

        IEnumerator rows = ((IEnumerable)_dataset.Values.First()).GetEnumerator();
        int count = 0;

        while (rows.MoveNext())
        {
            count++;
        }

        Assert.Equal(_origin.Values.First().AsEnumerable().Count(), count);
    }

    [Fact]
    public void ProjectionRunsThroughTheOriginsProvider()
    {
        Sample sample = Sample.Named("RunningTotalsQuery");
        PureQLProjection projection = new(
            [_dataset, new PureQLPostgreSqlSchemaDataSet(new AuditSchemaDataSet())],
            sample.Query
        );

        // The provider runs the rewritten tree in memory, where the marker
        // of a window throws when it is called: that is the provider's
        // refusal, and the projection runs its own tree.
        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, sample.Query),
                true
            ),
            ResultText.Of(projection, true)
        );
        Assert.Contains(
            "PostgreSqlWindows.Over(",
            TreeText.Of(Assert.Single(_provider.Queries)),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ProjectionWithoutWhatIsRewrittenIsAnsweredByTheProvider()
    {
        Sample sample = Sample.Named("UserNameColumnQuery");
        PureQLProjection projection = new([_dataset], sample.Query);

        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, sample.Query),
                false
            ),
            ResultText.Of(projection, false)
        );
        Assert.Equal(QueryExecution.Translated, projection.Execution);
        _ = Assert.Single(_provider.Queries);
    }
}
