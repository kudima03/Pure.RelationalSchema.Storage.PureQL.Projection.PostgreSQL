using System.Globalization;
using Pure.Primitives.Number;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Parameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.DataSet;
using PureQL.CSharp.Model;
using PureQL.CSharp.Model.RowExpressions;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;
using IParameter = Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Abstractions.Parameter.IParameter;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// The dataset's plumbing over the readings stored in PostgreSQL: the
// interpreter's filtered dataset outside and inside it, a dataset of a
// dataset, LINQ composed over its tables, and functions of closed operands
// (parameters), which the translator computes in .NET before it writes the
// statement. Needs Docker.
public sealed class PlumbingEdgeTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    private IStoredSchemaDataSet Stored => _database.Datasets[2];

    private ITable ReadingsTable => Stored.Keys.Single();

    [Fact]
    public void FilteredDatasetOverTheDatasetIsTranslated()
    {
        PureQLProjection actual = new(
            [
                Filtered(
                    new PureQLPostgreSqlSchemaDataSet(Stored),
                    PlumbingEdgeTests.LongLabels()
                ),
            ],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, PlumbingEdgeTests.LongLabels());
        Assert.Equal(QueryExecution.Translated, actual.Execution);
    }

    [Fact]
    public void DatasetOverAFilteredDatasetIsTranslated()
    {
        PureQLProjection actual = new(
            [
                new PureQLPostgreSqlSchemaDataSet(
                    Filtered(Stored, PlumbingEdgeTests.SmallIds())
                ),
            ],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, PlumbingEdgeTests.SmallIds());
        Assert.Equal(QueryExecution.Translated, actual.Execution);
    }

    [Fact]
    public void FilteredDatasetOverTheDatasetWithAFilterKeptInMemoryRunsInMemory()
    {
        PureQLProjection actual = new(
            [Filtered(new PureQLPostgreSqlSchemaDataSet(Stored), UpperApple())],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, UpperApple());
        Assert.Equal(QueryExecution.InMemory, actual.Execution);
    }

    // Observed: the window query is refused because of the filter's length,
    // which the rewriter does not visit, and its fallback then fails with
    // NotSupportedException (see the next test). Expected: translated, as in
    // the other order (FilteredDatasetOverTheDatasetIsTranslated).
    [Fact(
        Skip = "Bug: the filter of a PureQLFilteredSchemaDataSet origin is not rewritten, so the query is refused and its fallback fails"
    )]
    public void DatasetOverAFilteredDatasetWithARewrittenFunctionIsTranslated()
    {
        PureQLProjection actual = new(
            [
                new PureQLPostgreSqlSchemaDataSet(
                    Filtered(Stored, PlumbingEdgeTests.LongLabels())
                ),
            ],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, PlumbingEdgeTests.LongLabels());
        Assert.Equal(QueryExecution.Translated, actual.Execution);
    }

    // The query is refused (upper runs in memory), and the fallback reads the
    // filtered table, which asks PostgreSQL for its rows with the filter it
    // cannot translate. Observed: NotSupportedException from the fallback.
    // Expected: the result in memory, as without the rewriting dataset
    // (FilteredDatasetWithAFilterKeptInMemoryRunsInMemoryWithoutTheDataset).
    [Fact(
        Skip = "Bug: over a PureQLFilteredSchemaDataSet whose filter PostgreSQL refuses, the in-memory fallback fails with NotSupportedException"
    )]
    public void DatasetOverAFilteredDatasetWithAFilterKeptInMemoryRunsInMemory()
    {
        PureQLProjection actual = new(
            [new PureQLPostgreSqlSchemaDataSet(Filtered(Stored, UpperApple()))],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, UpperApple());
    }

    [Fact]
    public void FilteredDatasetWithAFilterKeptInMemoryRunsInMemoryWithoutTheDataset()
    {
        PureQLProjection actual = new(
            [Filtered(Stored, UpperApple())],
            PlumbingEdgeTests.WindowedQuery()
        );

        AssertFilteredResult(actual, UpperApple());
        Assert.Equal(QueryExecution.InMemory, actual.Execution);
    }

    [Fact]
    public void DatasetOfADatasetIsTranslated()
    {
        PureQLQuery query = PlumbingEdgeTests.WindowedQuery();
        PureQLProjection actual = new(
            [
                new PureQLPostgreSqlSchemaDataSet(
                    new PureQLPostgreSqlSchemaDataSet(Stored)
                ),
            ],
            query
        );

        Assert.Equal(
            ResultText.Of(new PureQLProjection(PostgreSqlDatabase.InMemory, query), true),
            ResultText.Of(actual, true)
        );
        Assert.Equal(QueryExecution.Translated, actual.Execution);
    }

    [Fact]
    public async Task LinqOverATableHasTheOriginsResult()
    {
        IStoredTableDataSet origin = Stored[ReadingsTable];
        IStoredTableDataSet table = new PureQLPostgreSqlSchemaDataSet(Stored)[
            ReadingsTable
        ];
        int rows = 0;

        await foreach (IRow _ in table)
        {
            rows++;
        }

        Assert.Equal(Queryable.Count(origin), Queryable.Count(table));
        Assert.Equal(Queryable.LongCount(origin), Queryable.LongCount(table));
        Assert.Equal(Queryable.Any(origin), Queryable.Any(table));
        Assert.Equal(
            Queryable.Take(origin, 3).AsEnumerable().Count(),
            Queryable.Take(table, 3).AsEnumerable().Count()
        );
        Assert.Equal(origin.AsEnumerable().Count(), rows);
    }

    // A subquery of LINQ over another table of the dataset, captured in the
    // lambda as LINQ captures it: the translator reads the captured table,
    // which is the dataset's, not the origin's. Observed: the dataset's query
    // fails where the origin's runs. Expected: the origin's result, as LINQ
    // composed over a table runs on the origin's provider.
    [Fact(
        Skip = "Bug: a dataset table captured in a LINQ lambda is not replaced by its origin, so the origin's provider refuses it"
    )]
    public void LinqWithACapturedTableHasTheOriginsResult()
    {
        IStoredTableDataSet origin = Stored[ReadingsTable];
        IStoredTableDataSet table = new PureQLPostgreSqlSchemaDataSet(Stored)[
            ReadingsTable
        ];

        Assert.Equal(
            Queryable.Count(Queryable.Where(origin, row => origin.Any())),
            Queryable.Count(Queryable.Where(table, row => table.Any()))
        );
    }

    [Fact]
    public void LinqWithACapturedTableOfTheOriginRuns()
    {
        IStoredTableDataSet origin = Stored[ReadingsTable];

        Assert.Equal(10, Queryable.Count(Queryable.Where(origin, row => origin.Any())));
    }

    // Functions of closed operands. The translator computes a closed part of
    // a tree in .NET, so the BCL shape of a rewritten function is computed by
    // .NET's meaning when its operands are parameters. Observed: the length
    // of "😀x" is 3 (UTF-16 units). Expected: 2, as in memory.
    [Fact(
        Skip = "Bug: the translator computes the rewritten length of a parameter in .NET, in UTF-16 units"
    )]
    public void LengthOfAParameterCountsCodePoints()
    {
        IReadOnlyList<string> result = Same(
            Valued(
                /*lang=json,strict*/"""{"operator": "length", "type": {"name": "integer"}, "operand": {"param": "text", "type": {"name": "string"} } }"""
            ),
            Text("😀x")
        );

        Assert.All(
            result.Skip(1),
            row => Assert.EndsWith(" | 2", row, StringComparison.Ordinal)
        );
    }

    // Observed: the position of "x" in "😀x" is 3. Expected: 2.
    [Fact(
        Skip = "Bug: the translator computes the rewritten position in a parameter in .NET, in UTF-16 units"
    )]
    public void PositionInAParameterCountsCodePoints()
    {
        IReadOnlyList<string> result = Same(
            Valued(
                /*lang=json,strict*/"""{"operator": "position", "type": {"name": "integer"}, "operand": {"param": "text", "type": {"name": "string"}}, "pattern": {"type": {"name": "string"}, "value": "x"}}"""
            ),
            Text("😀x")
        );

        Assert.All(
            result.Skip(1),
            row => Assert.EndsWith(" | 2", row, StringComparison.Ordinal)
        );
    }

    // A soft hyphen is not in "abc": position 0. .NET's IndexOf(string)
    // compares by the current culture, which ignores it. Observed: 1.
    // Expected: 0.
    [Fact(
        Skip = "Bug: the translator computes the rewritten position in a parameter with .NET's culture-sensitive IndexOf"
    )]
    public void PositionOfAnIgnorableCharacterInAParameterIsByCodePoints()
    {
        IReadOnlyList<string> result = Same(
            Valued(
                /*lang=json,strict*/"""{"operator": "position", "type": {"name": "integer"}, "operand": {"param": "text", "type": {"name": "string"}}, "pattern": {"type": {"name": "string"}, "value": "­"}}"""
            ),
            Text("abc")
        );

        Assert.All(
            result.Skip(1),
            row => Assert.EndsWith(" | 0", row, StringComparison.Ordinal)
        );
    }

    // Observed under sv-SE: "−12" (U+2212, the culture's minus sign).
    // Expected: "-12", as in memory.
    [Fact(
        Skip = "Bug: the translator computes the rewritten asString of a parameter with .NET's current culture"
    )]
    public void AsStringOfANegativeParameterIsItsLiteralInAnyCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

            IReadOnlyList<string> result = Same(
                Valued(
                    /*lang=json,strict*/"""{"operator": "asString", "type": {"name": "string"}, "operand": {"param": "number", "type": {"name": "integer"}}}"""
                ),
                new Dictionary<string, IParameter>
                {
                    ["number"] = new IntegerParameter(new Long(-12)),
                }
            );

            Assert.All(
                result.Skip(1),
                row => Assert.EndsWith(" | -12", row, StringComparison.Ordinal)
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void ReplaceOfAnEmptyPatternInAParameterLeavesIt()
    {
        _ = Same(
            Valued(
                /*lang=json,strict*/"""{"operator": "replace", "type": {"name": "string"}, "operand": {"param": "text", "type": {"name": "string"}}, "pattern": {"type": {"name": "string"}, "value": ""}, "replacement": {"type": {"name": "string"}, "value": "-"}}"""
            ),
            Text("abc")
        );
    }

    // Observed: OverflowException, as .NET's long % -1 overflows for
    // long.MinValue. Expected: 0, as in memory (and as PostgreSQL's numeric %
    // computes it).
    [Fact(
        Skip = "Bug: the translator computes the rewritten modulo of parameters in .NET, which overflows for long.MinValue % -1"
    )]
    public void ModuloOfTheSmallestIntegerByMinusOneIsZero()
    {
        _ = Same(
            Valued(
                $$$"""{"operator": "modulo", "type": {"name": "integer"}, "left": {"param": "number", "type": {"name": "integer"}}, "right": {{{Literal("integer", "-1")}}}}"""
            ),
            new Dictionary<string, IParameter>
            {
                ["number"] = new IntegerParameter(new Long(long.MinValue)),
            }
        );
    }

    [Fact]
    public void LengthOfAColumnCountsCodePoints()
    {
        IReadOnlyList<string> result = Same(
            Valued(
                $$$"""{"operator": "length", "type": {"name": "integer"}, "operand": {{{Field("label")}}}}"""
            ),
            new Dictionary<string, IParameter>()
        );

        Assert.Contains("5 | 2", result);
    }

    private static BooleanRow UpperApple()
    {
        return PlumbingEdgeTests.Condition(
            $$$"""{"operator": "equal", "type": {"name": "boolean"}, "left": {"operator": "upper", "type": {"name": "string"}, "operand": {{{Field("label")}}}}, "right": {"type": {"name": "string"}, "value": "APPLE"}}"""
        );
    }

    private static Dictionary<string, IParameter> Text(string value)
    {
        return new Dictionary<string, IParameter>
        {
            ["text"] = new StringParameter(new String(value)),
        };
    }

    private IReadOnlyList<string> Same(
        PureQLQuery query,
        IReadOnlyDictionary<string, IParameter> parameters
    )
    {
        PureQLProjection actual = new(_database.Rewriting, query, parameters);
        IReadOnlyList<string> result = ResultText.Of(actual, true);

        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, query, parameters),
                true
            ),
            result
        );

        return result;
    }

    private PureQLFilteredSchemaDataSet Filtered(
        IStoredSchemaDataSet origin,
        BooleanRow condition
    )
    {
        return new PureQLFilteredSchemaDataSet(
            origin,
            [KeyValuePair.Create(ReadingsTable, condition)]
        );
    }

    private static void AssertFilteredResult(
        PureQLProjection actual,
        BooleanRow condition
    )
    {
        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(
                    [
                        new PureQLFilteredSchemaDataSet(
                            Readings.Dataset,
                            [
                                KeyValuePair.Create(
                                    Readings.Dataset.Keys.Single(),
                                    condition
                                ),
                            ]
                        ),
                    ],
                    PlumbingEdgeTests.WindowedQuery()
                ),
                true
            ),
            ResultText.Of(actual, true)
        );

        if (actual.Execution == QueryExecution.InMemory)
        {
            _ = Assert.IsType<NotSupportedException>(actual.FallbackReason);
        }
    }
}
