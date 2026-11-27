using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Querying;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.Samples.SchemaDataSets;
using PureQL.CSharp.Model;
using PureQL.CSharp.Model.RowExpressions;
using PureQL.CSharp.Model.Serialization;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.DataSet;

// The edges of the dataset and its tables: a dataset of a dataset, the same
// table on every read and from every thread, the cancellation of asynchronous
// rows, errors of the origin that are not refusals, origins whose provider
// runs a tree in .NET (a projection read as a table), several origins in one
// query, and the interpreter's filtered dataset outside and inside.
public sealed class PlumbingEdgeTests
{
    private const string Window = "PostgreSqlWindows.Over(";

    private static readonly ITable ReadingsTable = Readings.Dataset.Keys.Single();

    [Fact]
    public void DatasetOfADatasetHandsTheOriginsTableToTheOriginsProvider()
    {
        RefusingProvider provider = new();
        IStoredSchemaDataSet origin = RefusingTable.Schema(
            new SchemaDataSetWithForeignKeys(),
            provider
        );
        PureQLPostgreSqlSchemaDataSet twice = new(
            new PureQLPostgreSqlSchemaDataSet(origin)
        );
        ITable key = origin.Keys.First();

        _ = Assert.Throws<NotSupportedException>(() =>
            Queryable.Where(twice[key], row => row.Cells.Count > 0).ToList()
        );

        Expression query = Assert.Single(provider.Queries);
        Assert.Same(origin[key], Root(query));
        Assert.DoesNotContain(Constants(query), value => value is RewritingTable);
    }

    [Fact]
    public void DatasetOfADatasetRunsAProjectionAsTheOrigin()
    {
        RefusingProvider provider = new();
        PureQLQuery query = Valued(
            $$$"""{"operator": "rowNumber", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {{{Field("id")}}}, "direction": "desc"}]}}"""
        );
        PureQLProjection projection = new(
            [
                new PureQLPostgreSqlSchemaDataSet(
                    new PureQLPostgreSqlSchemaDataSet(
                        RefusingTable.Schema(Readings.Dataset, provider)
                    )
                ),
            ],
            query
        );

        Assert.Equal(InMemory(query), ResultText.Of(projection, true));
        Assert.Equal(QueryExecution.InMemory, projection.Execution);
        Assert.Contains(
            Window,
            TreeText.Of(Assert.Single(provider.Queries)),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            Constants(Assert.Single(provider.Queries)),
            value => value is RewritingTable
        );
    }

    [Fact]
    public void DatasetOfADatasetInMemoryHoldsTheOriginsOwnTables()
    {
        IStoredSchemaDataSet origin = Readings.Dataset;
        PureQLPostgreSqlSchemaDataSet twice = new(
            new PureQLPostgreSqlSchemaDataSet(origin)
        );

        Assert.All(origin, table => Assert.Same(table.Value, twice[table.Key]));
    }

    [Fact]
    public void TableIsTheSameOnEveryRead()
    {
        PureQLPostgreSqlSchemaDataSet dataset = Refusing(out _);
        ITable key = dataset.Keys.First();

        Assert.Same(dataset[key], dataset[key]);
        Assert.True(dataset.TryGetValue(key, out IStoredTableDataSet? table));
        Assert.Same(dataset[key], table);
        IStoredTableDataSet[] first = [.. dataset.Values];
        IStoredTableDataSet[] second = [.. dataset.Values];

        Assert.Equal(first, second);
        Assert.All(dataset, pair => Assert.Same(dataset[pair.Key], pair.Value));
    }

    [Fact]
    public void TableIsFoundByAnEqualKeyThatIsAnotherInstance()
    {
        PureQLPostgreSqlSchemaDataSet dataset = Refusing(out _);
        ITable copy = new SchemaDataSetWithForeignKeys().Keys.First();

        Assert.NotSame(dataset.Keys.First(), copy);
        Assert.True(dataset.ContainsKey(copy));
        Assert.Same(dataset[dataset.Keys.First()], dataset[copy]);
    }

    [Fact]
    public void TablesAreTheSameFromEveryThread()
    {
        PureQLPostgreSqlSchemaDataSet dataset = Refusing(out _);
        ITable key = new SchemaDataSetWithForeignKeys().Keys.Last();
        IStoredTableDataSet[] tables = new IStoredTableDataSet[64];

        _ = Parallel.For(0, tables.Length, index => tables[index] = dataset[key]);

        Assert.All(tables, table => Assert.Same(tables[0], table));
    }

    [Fact]
    public void TableOfAnOriginWithoutTablesIsMissing()
    {
        IStoredSchemaDataSet origin = new StoredSchemaDataSet(
            Readings.Dataset.Schema,
            []
        );
        PureQLPostgreSqlSchemaDataSet dataset = new(origin);

        Assert.Empty(dataset);
        Assert.Empty(dataset.Keys);
        Assert.Empty(dataset.Values);
        Assert.False(dataset.ContainsKey(ReadingsTable));
        Assert.Equal(
            Record.Exception(() => origin[ReadingsTable])?.GetType(),
            Record.Exception(() => dataset[ReadingsTable])?.GetType()
        );
    }

    [Fact]
    public void DatasetEqualsItself()
    {
        PureQLPostgreSqlSchemaDataSet dataset = Refusing(out _);

        Assert.True(dataset.Equals(dataset));
        Assert.False(dataset.Equals(null));
        Assert.Equal(dataset, dataset with { });
    }

    [Fact]
    public void TextAndHashOfTheDatasetBehaveAsTheOrigins()
    {
        using NpgsqlConnection connection = new();
        IStoredSchemaDataSet origin = new PostgreSqlStoredSchemaDataSet(
            Readings.Dataset.Schema,
            connection
        );
        PureQLPostgreSqlSchemaDataSet dataset = new(origin);

        Assert.Equal(
            Record.Exception(() => _ = origin.GetHashCode())?.GetType(),
            Record.Exception(() => _ = dataset.GetHashCode())?.GetType()
        );
        Assert.Equal(
            Record.Exception(origin.ToString)?.GetType(),
            Record.Exception(dataset.ToString)?.GetType()
        );
    }

    [Fact]
    public async Task AsynchronousRowsGetTheCallersCancellationToken()
    {
        TokenTable origin = new(Readings.Dataset.Values.Single());
        PureQLPostgreSqlSchemaDataSet dataset = new(
            new StoredSchemaDataSet(Readings.Dataset.Schema, [origin])
        );
        using CancellationTokenSource source = new();
        List<IRow> rows = [];

        await foreach (IRow row in dataset[ReadingsTable].WithCancellation(source.Token))
        {
            rows.Add(row);
        }

        Assert.Equal(source.Token, origin.Token);
        Assert.Equal(Readings.Dataset.Values.Single().AsEnumerable(), rows);
    }

    [Fact]
    public async Task CancelledAsynchronousRowsAreCancelledAsTheOrigins()
    {
        TokenTable origin = new(Readings.Dataset.Values.Single());
        PureQLPostgreSqlSchemaDataSet dataset = new(
            new StoredSchemaDataSet(Readings.Dataset.Schema, [origin])
        );
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        Exception? fromOrigin = await Record.ExceptionAsync(() =>
            Drain(origin, source.Token)
        );
        Exception? fromDataset = await Record.ExceptionAsync(() =>
            Drain(dataset[ReadingsTable], source.Token)
        );

        Assert.Equal(fromOrigin?.GetType(), fromDataset?.GetType());
    }

    // An origin that fails otherwise than by refusing: the failure is the
    // query's, with or without the dataset, and nothing runs in memory.
    [Fact]
    public void FailureOfTheOriginThatIsNotARefusalIsTheQuerys()
    {
        IStoredSchemaDataSet origin = Failing();
        PureQLPostgreSqlSchemaDataSet dataset = new(origin);
        PureQLQuery query = Valued(Field("label"));

        _ = Assert.Throws<InvalidOperationException>(() =>
            new PureQLProjection([origin], query).ToList()
        );
        _ = Assert.Throws<InvalidOperationException>(() =>
            new PureQLProjection([dataset], query).ToList()
        );
        _ = Assert.Throws<InvalidOperationException>(() =>
            Queryable.Count(dataset[ReadingsTable])
        );
        _ = Assert.Throws<InvalidOperationException>(() =>
            dataset[ReadingsTable].Provider.CreateQuery(dataset[ReadingsTable].Expression)
        );
    }

    [Fact]
    public void FailureOfTheOriginInARewrittenTreeIsTheQuerys()
    {
        PureQLQuery query = Valued(
            $$$"""{"operator": "rowNumber", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {{{Field("id")}}}}]}}"""
        );

        _ = Assert.Throws<InvalidOperationException>(() =>
            new PureQLProjection(
                [new PureQLPostgreSqlSchemaDataSet(Failing())],
                query
            ).ToList()
        );
    }

    // A projection is a table too; read as one, its provider runs a tree in
    // memory with the interpreter's operators. Through the dataset it is given
    // the rewritten tree instead, whose BCL shapes .NET computes otherwise
    // than PostgreSQL: string.Length counts UTF-16 units, so the length of
    // "😀x" is 3, not 2. Observed: 3 for the row of id 5. Expected: 2, the
    // interpreter's result without the dataset.
    [Fact(
        Skip = "Bug: an origin whose provider runs trees in .NET (a projection read as a table) is given the rewritten tree and computes length in UTF-16 units"
    )]
    public void ProjectionReadAsATableGivesTheInterpretersResult()
    {
        AssertSameOverAView(
            Viewed(
                /*lang=json,strict*/"""{"operator": "length", "type": {"name": "integer"}, "operand": {"source": "v.view", "field": "label", "type": {"name": "string"} } }"""
            )
        );
    }

    // Observed: ArgumentException from the interpreter's LocalProvider, which
    // cannot run PostgreSqlWindows.Over over its in-memory rows; it is not a
    // NotSupportedException, so the query fails instead of running in memory.
    // Expected: the interpreter's result, as without the dataset.
    [Fact(
        Skip = "Bug: an origin whose provider runs trees in .NET (a projection read as a table) is given the window marker and fails with ArgumentException"
    )]
    public void ProjectionReadAsATableWithAWindowGivesTheInterpretersResult()
    {
        AssertSameOverAView(
            Viewed(
                /*lang=json,strict*/"""{"operator": "rowNumber", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {"source": "v.view", "field": "label", "type": {"name": "string"} } }]}}"""
            )
        );
    }

    [Fact]
    public void ProjectionReadAsATableWithoutRewritesGivesTheInterpretersResult()
    {
        AssertSameOverAView(
            Viewed(
                /*lang=json,strict*/"""{"source": "v.view", "field": "label", "type": {"name": "string"}}"""
            )
        );
    }

    // A query of two schemas, each stored behind a provider of its own, goes
    // to the provider of the first table, with both origins' tables in it.
    [Fact]
    public void QueryOverTwoOriginsReadsBothOriginsTables()
    {
        RefusingProvider readings = new();
        RefusingProvider users = new();
        IStoredSchemaDataSet[] datasets =
        [
            new PureQLPostgreSqlSchemaDataSet(
                RefusingTable.Schema(Readings.Dataset, readings)
            ),
            new PureQLPostgreSqlSchemaDataSet(
                RefusingTable.Schema(new SchemaDataSetWithForeignKeys(), users)
            ),
        ];
        PureQLQuery query = Query(
            $$$"""
            {
              "from": {"entity": "t.readings"},
              "joins": [
                {"kind": "inner", "entity": "schema_with_foreign_keys.users", "on": {"type": {"name": "boolean"}, "value": true}}
              ],
              "select": [
                {"alias": "id", "expression": {{{Field("id")}}}},
                {"alias": "user", "expression": {"source": "schema_with_foreign_keys.users", "field": "user_id", "type": {"name": "uuid"} } },
                {"alias": "rank", "expression": {"operator": "rank", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {{{Field(
                "id"
            )}}}}]} } }
              ]
            }
            """
        );
        PureQLProjection projection = new(datasets, query);

        Assert.Equal(InMemory(query, false), ResultText.Of(projection, false));
        Expression tree = Assert.Single(readings.Queries);
        Assert.Empty(users.Queries);
        Assert.Equal(2, Constants(tree).Count(value => value is RefusingTable));
        Assert.DoesNotContain(Constants(tree), value => value is RewritingTable);
    }

    [Fact]
    public void LinqComposedOverATableHasTheOriginsResult()
    {
        PassingTable.Recorder provider = new();
        IStoredSchemaDataSet origin = PassingTable.Schema(Readings.Dataset, provider);
        IStoredTableDataSet table = new PureQLPostgreSqlSchemaDataSet(origin)[
            ReadingsTable
        ];
        IEnumerable<IRow> rows = origin[ReadingsTable].AsEnumerable();

        Assert.Equal(
            rows.Where(row => row.Cells.Count > 0).Skip(2).Take(3).Select(Text),
            Queryable
                .Take(
                    Queryable.Skip(Queryable.Where(table, row => row.Cells.Count > 0), 2),
                    3
                )
                .AsEnumerable()
                .Select(Text)
        );
        Assert.Equal(rows.First(), Queryable.First(table));
        Assert.Equal(rows.LongCount(), Queryable.LongCount(table));
        Assert.True(Queryable.Any(table, row => row.Cells.Count == 9));
        Assert.Equal(
            rows.Sum(row => row.Cells.Count),
            Queryable.Sum(Queryable.Select(table, row => row.Cells.Count))
        );
        Assert.All(
            provider.Queries,
            query => Assert.Same(origin[ReadingsTable], Root(query))
        );
    }

    [Fact]
    public void LinqOverTheSameTableTwiceReadsTheOriginTwice()
    {
        RefusingProvider provider = new();
        IStoredSchemaDataSet origin = RefusingTable.Schema(Readings.Dataset, provider);
        IStoredTableDataSet table = new PureQLPostgreSqlSchemaDataSet(origin)[
            ReadingsTable
        ];
        Expression joined = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Concat),
            [typeof(IRow)],
            table.Expression,
            table.Expression
        );

        _ = Assert.Throws<NotSupportedException>(() =>
            table.Provider.CreateQuery<IRow>(joined)
        );

        Expression query = Assert.Single(provider.Queries);
        Assert.Equal(
            2,
            Constants(query).Count(value => ReferenceEquals(value, origin[ReadingsTable]))
        );
    }

    // The interpreter's filtered dataset over the rewriting dataset: the
    // filter is written over the dataset's table, so the provider is given the
    // rewritten tree with the filter in it.
    [Fact]
    public void FilteredDatasetOverTheDatasetIsRewrittenWithItsFilter()
    {
        RefusingProvider provider = new();
        PureQLQuery query = WindowedQuery();
        IStoredSchemaDataSet filtered = new PureQLFilteredSchemaDataSet(
            new PureQLPostgreSqlSchemaDataSet(
                RefusingTable.Schema(Readings.Dataset, provider)
            ),
            [KeyValuePair.Create(ReadingsTable, LongLabels())]
        );
        PureQLProjection projection = new([filtered], query);

        Assert.Equal(
            InMemoryFiltered(query, LongLabels()),
            ResultText.Of(projection, true)
        );
        string tree = TreeText.Of(Assert.Single(provider.Queries));
        Assert.Contains(Window, tree, StringComparison.Ordinal);
        Assert.DoesNotContain("PureQLText.", tree, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Constants(Assert.Single(provider.Queries)),
            value => value is RewritingTable
        );
    }

    // The rewriting dataset over the interpreter's filtered dataset: the
    // provider is given the filter in the rewritten tree, and the in-memory
    // fallback reads the filtered rows from the origin's provider.
    [Fact]
    public void DatasetOverAFilteredDatasetKeepsTheFilter()
    {
        PassingTable.Recorder provider = new();
        PureQLQuery query = WindowedQuery();
        IStoredSchemaDataSet dataset = new PureQLPostgreSqlSchemaDataSet(
            new PureQLFilteredSchemaDataSet(
                PassingTable.Schema(Readings.Dataset, provider),
                [KeyValuePair.Create(ReadingsTable, SmallIds())]
            )
        );
        PureQLProjection projection = new([dataset], query);

        Assert.Equal(
            InMemoryFiltered(query, SmallIds()),
            ResultText.Of(projection, true)
        );
        string tree = TreeText.Of(provider.Queries[0]);
        Assert.Contains(Window, tree, StringComparison.Ordinal);
        Assert.Equal(2, tree.Split(".Where(").Length);
    }

    // The filter of a filtered dataset under the rewriting dataset is the
    // origin's expression, which the rewriter puts in the tree without
    // visiting it: a function in the filter stays the interpreter's, and the
    // provider refuses a tree it would translate in the other order.
    // Observed: PureQLText.Length in the tree given to the provider.
    // Expected: the filter rewritten as the rest of the tree.
    [Fact(
        Skip = "Bug: the filter of a PureQLFilteredSchemaDataSet origin is not rewritten"
    )]
    public void DatasetOverAFilteredDatasetRewritesTheFilter()
    {
        PassingTable.Recorder provider = new();
        PureQLQuery query = WindowedQuery();
        IStoredSchemaDataSet dataset = new PureQLPostgreSqlSchemaDataSet(
            new PureQLFilteredSchemaDataSet(
                PassingTable.Schema(Readings.Dataset, provider),
                [KeyValuePair.Create(ReadingsTable, LongLabels())]
            )
        );
        PureQLProjection projection = new([dataset], query);

        Assert.Equal(
            InMemoryFiltered(query, LongLabels()),
            ResultText.Of(projection, true)
        );
        string tree = TreeText.Of(provider.Queries[0]);
        Assert.Contains(Window, tree, StringComparison.Ordinal);
        Assert.DoesNotContain("PureQLText.", tree, StringComparison.Ordinal);
    }

    // Under the rewriting dataset a filtered table is a table of its own, so
    // the in-memory fallback enumerates it, and the filtered table asks the
    // origin's provider for its rows: a provider that refuses the filter (as
    // PostgreSQL's refuses a function it cannot translate) then fails the
    // query, which runs in memory without the rewriting dataset, where the
    // fallback reads the stored rows and filters them itself. Observed:
    // NotSupportedException thrown by the fallback. Expected: the filtered
    // dataset's result.
    [Fact(
        Skip = "Bug: over a PureQLFilteredSchemaDataSet whose provider refuses the filter, the in-memory fallback fails with NotSupportedException"
    )]
    public void DatasetOverAFilteredDatasetRunsInMemoryAsTheFilteredDataset()
    {
        PureQLQuery query = WindowedQuery();
        IReadOnlyList<string> expected = ResultText.Of(
            new PureQLProjection(
                [
                    new PureQLFilteredSchemaDataSet(
                        RefusingTable.Schema(Readings.Dataset, new RefusingProvider()),
                        [KeyValuePair.Create(ReadingsTable, SmallIds())]
                    ),
                ],
                query
            ),
            true
        );
        PureQLProjection actual = new(
            [
                new PureQLPostgreSqlSchemaDataSet(
                    new PureQLFilteredSchemaDataSet(
                        RefusingTable.Schema(Readings.Dataset, new RefusingProvider()),
                        [KeyValuePair.Create(ReadingsTable, SmallIds())]
                    )
                ),
            ],
            query
        );

        Assert.Equal(InMemoryFiltered(query, SmallIds()), expected);
        Assert.Equal(expected, ResultText.Of(actual, true));
    }

    [Fact]
    public void FilteredDatasetBetweenTwoDatasetsKeepsTheFilterOnce()
    {
        PassingTable.Recorder provider = new();
        PureQLQuery query = WindowedQuery();
        IStoredSchemaDataSet dataset = new PureQLPostgreSqlSchemaDataSet(
            new PureQLFilteredSchemaDataSet(
                new PureQLPostgreSqlSchemaDataSet(
                    PassingTable.Schema(Readings.Dataset, provider)
                ),
                [KeyValuePair.Create(ReadingsTable, SmallIds())]
            )
        );
        PureQLProjection projection = new([dataset], query);

        Assert.Equal(
            InMemoryFiltered(query, SmallIds()),
            ResultText.Of(projection, true)
        );
        Expression tree = provider.Queries[0];
        Assert.Equal(2, TreeText.Of(tree).Split(".Where(").Length);
        Assert.Contains(Window, TreeText.Of(tree), StringComparison.Ordinal);
        Assert.DoesNotContain(Constants(tree), value => value is RewritingTable);
    }

    internal static BooleanRow Condition(string json)
    {
        JsonSerializerOptions options = new() { MaxDepth = 256 };

        foreach (JsonConverter converter in new PureQLConverters())
        {
            options.Converters.Add(converter);
        }

        return JsonSerializer.Deserialize<BooleanRow>(json, options);
    }

    internal static BooleanRow LongLabels()
    {
        return Condition(
            $$$"""{"operator": "greaterThan", "type": {"name": "boolean"}, "left": {"operator": "length", "type": {"name": "integer"}, "operand": {{{Field("label")}}}}, "right": {{{Literal("integer", "1")}}}}"""
        );
    }

    internal static BooleanRow SmallIds()
    {
        return Condition(
            $$$"""{"operator": "lessThanOrEqual", "type": {"name": "boolean"}, "left": {{{Field("id")}}}, "right": {{{Literal("integer", "6")}}}}"""
        );
    }

    internal static PureQLQuery WindowedQuery()
    {
        return Valued(
            $$$"""{"operator": "rowNumber", "type": {"name": "integer"}, "over": {"partitionBy": [{{{Field("grp")}}}], "orderBy": [{"expression": {{{Field("id")}}}, "direction": "desc"}]}}"""
        );
    }

    private static IReadOnlyList<string> InMemoryFiltered(
        PureQLQuery query,
        BooleanRow condition
    )
    {
        return ResultText.Of(
            new PureQLProjection(
                [
                    new PureQLFilteredSchemaDataSet(
                        Readings.Dataset,
                        [KeyValuePair.Create(ReadingsTable, condition)]
                    ),
                ],
                query
            ),
            true
        );
    }

    private static void AssertSameOverAView(PureQLQuery query)
    {
        IStoredSchemaDataSet view = View();

        Assert.Equal(
            ResultText.Of(new PureQLProjection([view], query), true),
            ResultText.Of(
                new PureQLProjection([new PureQLPostgreSqlSchemaDataSet(view)], query),
                true
            )
        );
    }

    // The readings' ids and labels as a projection, read as table v.view.
    private static IStoredSchemaDataSet View()
    {
        PureQLProjection projection = new(
            PostgreSqlDatabase.InMemory,
            Query(
                $$$"""
                {
                  "from": {"entity": "t.readings"},
                  "select": [
                    {"alias": "id", "expression": {{{Field("id")}}}},
                    {"alias": "label", "expression": {{{Field("label")}}}}
                  ]
                }
                """
            )
        );
        ITable table = new Table.Table(
            new String("view"),
            projection.TableSchema.Columns,
            []
        );

        return new StoredSchemaDataSet(
            new Schema.Schema(new String("v"), [table], []),
            [new ViewTable(projection, table)]
        );
    }

    private static PureQLQuery Viewed(string value)
    {
        return Query(
            $$$"""
            {
              "from": {"entity": "v.view"},
              "select": [
                {"alias": "id", "expression": {"source": "v.view", "field": "id", "type": {"name": "integer"} } },
                {"alias": "value", "expression": {{{value}}}}
              ],
              "orderBy": [{"expression": {"source": "v.view", "field": "id", "type": {"name": "integer"} } }]
            }
            """
        );
    }

    private static IReadOnlyList<string> InMemory(PureQLQuery query, bool ordered = true)
    {
        return ResultText.Of(
            new PureQLProjection(PostgreSqlDatabase.InMemory, query),
            ordered
        );
    }

    private static PureQLPostgreSqlSchemaDataSet Refusing(out RefusingProvider provider)
    {
        provider = new RefusingProvider();
        return new PureQLPostgreSqlSchemaDataSet(
            RefusingTable.Schema(new SchemaDataSetWithForeignKeys(), provider)
        );
    }

    private static IStoredSchemaDataSet Failing()
    {
        return new StoredSchemaDataSet(
            Readings.Dataset.Schema,
            [new FailingTable(Readings.Dataset.Values.Single())]
        );
    }

    private static string Text(IRow row)
    {
        return string.Join(
            "|",
            row.Cells.OrderBy(cell => cell.Key.Name.TextValue, StringComparer.Ordinal)
                .Select(cell => cell.Value.Value.TextValue)
        );
    }

    private static async Task Drain(IAsyncEnumerable<IRow> rows, CancellationToken token)
    {
        await foreach (IRow _ in rows.WithCancellation(token))
        {
            token.ThrowIfCancellationRequested();
        }
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

    private static IReadOnlyList<object?> Constants(Expression tree)
    {
        ConstantValues values = new();
        _ = values.Visit(tree);
        return values.Values;
    }

    private sealed class ConstantValues : ExpressionVisitor
    {
        public List<object?> Values { get; } = [];

        protected override Expression VisitConstant(ConstantExpression node)
        {
            Values.Add(node.Value);
            return node;
        }
    }

    // A table read from a projection, through the projection's provider.
    private sealed class ViewTable(PureQLProjection rows, ITable schema)
        : IStoredTableDataSet
    {
        private readonly PureQLProjection _rows = rows;

        public ITable TableSchema { get; } = schema;

        public Type ElementType => typeof(IRow);

        public Expression Expression =>
            Expression.Constant(this, typeof(IQueryable<IRow>));

        public IQueryProvider Provider => _rows.Provider;

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

    // A table behind a provider of its own that remembers the token its
    // asynchronous rows are read with.
    private sealed class TokenTable(IStoredTableDataSet rows) : IStoredTableDataSet
    {
        private readonly IStoredTableDataSet _rows = rows;

        public CancellationToken Token { get; private set; }

        public ITable TableSchema => _rows.TableSchema;

        public Type ElementType => typeof(IRow);

        public Expression Expression =>
            Expression.Constant(this, typeof(IQueryable<IRow>));

        public IQueryProvider Provider { get; } = new RefusingProvider();

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
            Token = cancellationToken;
            return _rows.GetAsyncEnumerator(cancellationToken);
        }
    }

    // A table whose provider fails every query with an error that is not a
    // refusal.
    private sealed class FailingTable(IStoredTableDataSet rows) : IStoredTableDataSet
    {
        private readonly IStoredTableDataSet _rows = rows;

        public ITable TableSchema => _rows.TableSchema;

        public Type ElementType => typeof(IRow);

        public Expression Expression =>
            Expression.Constant(this, typeof(IQueryable<IRow>));

        public IQueryProvider Provider { get; } = new FailingProvider();

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

    private sealed class FailingProvider : IQueryProvider
    {
        public IQueryable CreateQuery(Expression expression)
        {
            throw new InvalidOperationException("The connection is closed.");
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            throw new InvalidOperationException("The connection is closed.");
        }

        public object? Execute(Expression expression)
        {
            throw new InvalidOperationException("The connection is closed.");
        }

        public TResult Execute<TResult>(Expression expression)
        {
            throw new InvalidOperationException("The connection is closed.");
        }
    }
}
