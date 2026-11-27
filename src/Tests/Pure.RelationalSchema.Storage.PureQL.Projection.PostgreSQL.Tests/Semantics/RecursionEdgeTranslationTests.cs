using System.Collections;
using System.Globalization;
using System.Reflection;
using Pure.Primitives.Number;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Parameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using PureQL.CSharp.Model;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;
using IParameter = Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Abstractions.Parameter.IParameter;
using ModelRecursion = PureQL.CSharp.Model.Recursion;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// The edges of recursive subqueries in PostgreSQL: depths from parameters and
// at the end of an integer, empty seeds, steps that never end, recursions
// that hold windows and functions or are read by them, two recursions, a step
// over a join, a grouped step, seeds of inline rows and of another recursion,
// and nulls through the levels; each with exactly the interpreter's result in
// memory, or failing as it fails. Needs Docker.
public sealed class RecursionEdgeTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private static readonly string True =
        /*lang=json,strict*/"""{"type": {"name": "boolean"}, "value": true}""";

    private readonly PostgreSqlDatabase _database = database;

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    [InlineData(1, 1)]
    public void MaxDepthOfAParameterIsTheLimit(int last, long maxDepth)
    {
        PureQLProjection actual = Same(
            Walk( /*lang=json,strict*/
                """{"param": "depth", "type": {"name": "integer"}}""",
                Before(last)
            ),
            Depth(maxDepth)
        );

        Assert.Equal(QueryExecution.Translated, actual.Execution);
        Assert.Equal(last + 2, ResultText.Of(actual, true).Count);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    public void MaxDepthOfAParameterExceededFails(int last, long maxDepth)
    {
        Fails(
            Walk( /*lang=json,strict*/
                """{"param": "depth", "type": {"name": "integer"}}""",
                Before(last)
            ),
            Depth(maxDepth)
        );
    }

    [Fact]
    public void MaxDepthOfTheLargestIntegerIsNoLimit()
    {
        PureQLProjection actual = Same(
            Walk(long.MaxValue.ToString(CultureInfo.InvariantCulture), Before(4))
        );

        Assert.Equal(QueryExecution.Translated, actual.Execution);
        Assert.Equal(6, ResultText.Of(actual, true).Count);
    }

    [Fact]
    public void EmptySeedGivesNoRows()
    {
        PureQLProjection actual = Same(Walk("5", Before(3), seedBound: "0"));

        Assert.Equal(QueryExecution.Translated, actual.Execution);
        _ = Assert.Single(ResultText.Of(actual, true));
    }

    [Fact]
    public void StepThatNeverEndsFailsAtTheDepth()
    {
        Fails(Walk("5", True, id: Level("id")));
    }

    [Fact]
    public void StepThatCyclesBetweenTwoRowsFailsAtTheDepth()
    {
        Fails(
            Walk(
                "7",
                True,
                id: $$$"""{"operator": "subtract", "type": {"name": "integer"}, "values": [{{{Literal("integer", "3")}}}, {{{Level("id")}}}]}"""
            )
        );
    }

    [Fact]
    public void WindowInTheSeedGivesTheInterpretersResult()
    {
        _ = Same(
            Walk(
                "5",
                Before(2),
                seedId: $$$"""{"operator": "rowNumber", "type": {"name": "integer"}, "over": {"orderBy": [{"expression": {{{Field("id")}}}, "direction": "desc"}]}}""",
                seedBound: "10"
            )
        );
    }

    [Fact]
    public void WindowOverTheRecursionGivesTheInterpretersResult()
    {
        _ = Same(
            Query(
                $$$"""
                {
                  "subqueries": [{{{Recursive(
                    "walk",
                    "5",
                    Seed("3"),
                    Next("walk", Before(2), Plus(Level("id"), 1))
                )}}}],
                  "from": {"subquery": "walk"},
                  "select": [
                    {"alias": "depth", "expression": {{{Level("depth")}}}},
                    {"alias": "id", "expression": {{{Level("id")}}}},
                    {"alias": "rank", "expression": {"operator": "rank", "type": {"name": "integer"}, "over": {"partitionBy": [{{{Level(
                    "depth"
                )}}}], "orderBy": [{"expression": {{{Level(
                    "id"
                )}}}, "direction": "desc"}]} } }
                  ],
                  "orderBy": [{"expression": {{{Level(
                    "depth"
                )}}}}, {"expression": {{{Level("id")}}}}]
                }
                """
            )
        );
    }

    [Fact]
    public void FunctionInTheStepIsTranslated()
    {
        PureQLProjection actual = Same(
            Walk(
                "5",
                Before(3),
                id: $$$"""{"operator": "modulo", "type": {"name": "integer"}, "left": {{{Plus(Level("id"), 7)}}}, "right": {{{Literal("integer", "5")}}}}""",
                seedBound: "4"
            )
        );

        Assert.Equal(QueryExecution.Translated, actual.Execution);
    }

    // The text of the reading of id 5, "😀x", one code point more on each
    // level while its length in code points is below 5.
    [Fact]
    public void LengthInTheStepCountsCodePoints()
    {
        string text =
            /*lang=json,strict*/"""{"source": "walk", "field": "text", "type": {"name": "string"}}""";
        string length =
            $$$"""{"operator": "length", "type": {"name": "integer"}, "operand": {{{text}}}}""";
        PureQLProjection actual = Same(
            Query(
                $$$"""
                {
                  "subqueries": [
                    {
                      "name": "walk",
                      "recursive": {
                        "seed": {
                          "from": {"entity": "t.readings"},
                          "where": {"operator": "equal", "type": {"name": "boolean"}, "left": {{{Field(
                    "id"
                )}}}, "right": {{{Literal("integer", "5")}}}},
                          "select": [{"alias": "text", "expression": {{{Field(
                    "label"
                )}}}}]
                        },
                        "next": {
                          "from": {"subquery": "walk"},
                          "where": {"operator": "lessThan", "type": {"name": "boolean"}, "left": {{{length}}}, "right": {{{Literal(
                    "integer",
                    "5"
                )}}}},
                          "select": [{"alias": "text", "expression": {"operator": "concat", "type": {"name": "string"}, "values": [{{{text}}}, {"type": {"name": "string"}, "value": "😀"}]}}]
                        },
                        "maxDepth": 10
                      }
                    }
                  ],
                  "from": {"subquery": "walk"},
                  "select": [
                    {"alias": "text", "expression": {{{text}}}},
                    {"alias": "length", "expression": {{{length}}}}
                  ],
                  "orderBy": [{"expression": {{{length}}}}]
                }
                """
            )
        );

        Assert.Equal(
            [
                "text:string | length:long",
                "😀x | 2",
                "😀x😀 | 3",
                "😀x😀😀 | 4",
                "😀x😀😀😀 | 5",
            ],
            ResultText.Of(actual, true)
        );
    }

    [Fact]
    public void TwoRecursionsJoinedGiveTheInterpretersResult()
    {
        _ = Same(
            Query(
                $$$"""
                {
                  "subqueries": [
                    {{{Recursive(
                    "first",
                    "5",
                    Seed("2"),
                    Next("first", Before(3, "first"), Plus(Level("id", "first"), 1))
                )}}},
                    {{{Recursive(
                    "second",
                    "5",
                    Seed("3"),
                    Next("second", Before(2, "second"), Plus(Level("id", "second"), 2))
                )}}}
                  ],
                  "from": {"subquery": "first"},
                  "joins": [
                    {"kind": "inner", "subquery": "second", "on": {"operator": "equal", "type": {"name": "boolean"}, "left": {{{Level(
                    "id",
                    "first"
                )}}}, "right": {{{Level("id", "second")}}}}}
                  ],
                  "select": [
                    {"alias": "id", "expression": {{{Level("id", "first")}}}},
                    {"alias": "a", "expression": {{{Level("depth", "first")}}}},
                    {"alias": "b", "expression": {{{Level("depth", "second")}}}}
                  ],
                  "orderBy": [{"expression": {{{Level(
                    "id",
                    "first"
                )}}}}, {"expression": {{{Level(
                    "depth",
                    "first"
                )}}}}, {"expression": {{{Level("depth", "second")}}}}]
                }
                """
            )
        );
    }

    // Each level the reading of the next id, until there is none.
    [Fact]
    public void StepOverAJoinWithAStoredTableIsTranslated()
    {
        string next = $$$"""
            {
              "from": {"subquery": "walk"},
              "joins": [
                {"kind": "inner", "entity": "t.readings", "on": {"operator": "equal", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Plus(Level("id"), 1)}}}}}
              ],
              "select": [
                {"alias": "depth", "expression": {{{Plus(Level("depth"), 1)}}}},
                {"alias": "id", "expression": {{{Field("id")}}}}
              ]
            }
            """;
        PureQLProjection actual = Same(WalkOf(Recursive("walk", "20", Seed("1"), next)));

        Assert.Equal(QueryExecution.Translated, actual.Execution);
        Assert.Equal(11, ResultText.Of(actual, true).Count);
    }

    [Fact]
    public void StepOverAJoinDeeperThanTheDepthFails()
    {
        string next = $$$"""
            {
              "from": {"subquery": "walk"},
              "joins": [
                {"kind": "inner", "entity": "t.readings", "on": {"operator": "equal", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Plus(Level("id"), 1)}}}}}
              ],
              "select": [
                {"alias": "depth", "expression": {{{Plus(Level("depth"), 1)}}}},
                {"alias": "id", "expression": {{{Field("id")}}}}
              ]
            }
            """;

        Fails(WalkOf(Recursive("walk", "8", Seed("1"), next)));
    }

    // A grouped step is refused by the translator (PostgreSQL allows no
    // aggregate in the recursive term) and runs in memory.
    [Fact]
    public void GroupedStepRunsInMemory()
    {
        string next = $$$"""
            {
              "from": {"subquery": "walk"},
              "where": {{{Before(3)}}},
              "groupBy": [
                {"alias": "depth", "expression": {{{Plus(Level("depth"), 1)}}}},
                {"alias": "id", "expression": {{{Level("id")}}}}
              ],
              "select": [
                {"alias": "depth", "expression": {"key": "depth", "type": {"name": "integer"} } },
                {"alias": "id", "expression": {"key": "id", "type": {"name": "integer"} } }
              ]
            }
            """;
        PureQLProjection actual = Same(WalkOf(Recursive("walk", "5", Seed("10"), next)));

        Assert.Equal(QueryExecution.InMemory, actual.Execution);
    }

    [Fact]
    public void SeedOfInlineRowsWithAStoredStepGivesTheInterpretersResult()
    {
        string seed = /*lang=json,strict*/
            """
            {
              "from": {"subquery": "start"},
              "select": [
                {"alias": "depth", "expression": {"type": {"name": "integer"}, "value": 0}},
                {"alias": "id", "expression": {"source": "start", "field": "id", "type": {"name": "integer"} } }
              ]
            }
            """;
        string next = $$$"""
            {
              "from": {"subquery": "walk"},
              "joins": [
                {"kind": "inner", "entity": "t.readings", "on": {"operator": "equal", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Plus(Level("id"), 2)}}}}}
              ],
              "select": [
                {"alias": "depth", "expression": {{{Plus(Level("depth"), 1)}}}},
                {"alias": "id", "expression": {{{Field("id")}}}}
              ]
            }
            """;
        string start = /*lang=json,strict*/
            """
            {"name": "start", "rows": [
              [{"alias": "id", "expression": {"type": {"name": "integer"}, "value": 1}}],
              [{"alias": "id", "expression": {"type": {"name": "integer"}, "value": 4}}]
            ]}
            """;

        _ = Same(WalkOf(start + ", " + Recursive("walk", "10", seed, next)));
    }

    [Fact]
    public void RecursionReadByInGivesTheInterpretersResult()
    {
        PureQLProjection actual = Same(
            Query(
                $$$"""
                {
                  "subqueries": [{{{Recursive(
                    "walk",
                    "5",
                    Seed("2"),
                    Next("walk", Before(2), Plus(Level("id"), 3))
                )}}}],
                  "from": {"entity": "t.readings"},
                  "where": {"operator": "in", "type": {"name": "boolean"}, "operand": {{{Field(
                    "id"
                )}}}, "list": {"subquery": "walk", "field": "id", "type": {"name": "integer"} } },
                  "select": [{"alias": "id", "expression": {{{Field("id")}}}}],
                  "orderBy": [{"expression": {{{Field("id")}}}}]
                }
                """
            )
        );

        Assert.Equal(
            ["id:long", "1", "2", "4", "5", "7", "8"],
            ResultText.Of(actual, true)
        );
    }

    [Fact]
    public void RecursionSeededByAnotherGivesTheInterpretersResult()
    {
        string seed = /*lang=json,strict*/
            """
            {
              "from": {"subquery": "first"},
              "select": [
                {"alias": "depth", "expression": {"type": {"name": "integer"}, "value": 0}},
                {"alias": "id", "expression": {"source": "first", "field": "id", "type": {"name": "integer"} } }
              ]
            }
            """;

        _ = Same(
            WalkOf(
                Recursive(
                    "first",
                    "5",
                    Seed("2"),
                    Next("first", Before(2, "first"), Plus(Level("id", "first"), 1))
                )
                    + ", "
                    + Recursive(
                        "walk",
                        "5",
                        seed,
                        Next("walk", Before(1), Plus(Level("id"), 10))
                    )
            )
        );
    }

    [Fact]
    public void NullsGoThroughTheLevels()
    {
        string n =
            /*lang=json,strict*/"""{"source": "walk", "field": "n", "type": {"name": "integer", "nullable": true}}""";
        string seed = $$$"""
            {
              "from": {"entity": "t.readings"},
              "select": [
                {"alias": "depth", "expression": {{{Literal("integer", "0")}}}},
                {"alias": "id", "expression": {{{Field("id")}}}},
                {"alias": "n", "expression": {{{Field("n")}}}}
              ]
            }
            """;
        string next = $$$"""
            {
              "from": {"subquery": "walk"},
              "where": {{{Before(2)}}},
              "select": [
                {"alias": "depth", "expression": {{{Plus(Level("depth"), 1)}}}},
                {"alias": "id", "expression": {{{Level("id")}}}},
                {"alias": "n", "expression": {"operator": "multiply", "type": {"name": "integer", "nullable": true}, "values": [{{{n}}}, {{{Literal(
                "integer",
                "2"
            )}}}]}}
              ]
            }
            """;

        PureQLProjection actual = Same(
            Query(
                $$$"""
                {
                  "subqueries": [{{{Recursive("walk", "5", seed, next)}}}],
                  "from": {"subquery": "walk"},
                  "select": [
                    {"alias": "depth", "expression": {{{Level("depth")}}}},
                    {"alias": "id", "expression": {{{Level("id")}}}},
                    {"alias": "n", "expression": {{{n}}}}
                  ],
                  "orderBy": [{"expression": {{{Level(
                    "depth"
                )}}}}, {"expression": {{{Level("id")}}}}]
                }
                """
            )
        );

        Assert.Equal(QueryExecution.Translated, actual.Execution);
        Assert.Contains("2 | 3 | ", ResultText.Of(actual, true));
    }

    // PureQL's JSON rejects a maxDepth below 1, but a model built in code
    // holds what it is given, and the interpreter fails a depth below 1 when
    // it runs the recursion, whatever its rows. Observed: through the dataset
    // PostgreSQL returns the seed (WITH RECURSIVE at depth 0 fails only a row
    // of level 1, and the step gives none). Expected: the query fails as in
    // memory.
    [Fact(
        Skip = "Bug: a maxDepth of 0 runs in PostgreSQL, where the interpreter fails it"
    )]
    public void MaxDepthOfZeroFailsAsInMemory()
    {
        PureQLQuery query = Walk("1", Before(0));

        SetMaxDepth(query, 0L);

        Fails(query);
    }

    private static Dictionary<string, IParameter> Depth(long value)
    {
        return new Dictionary<string, IParameter>
        {
            ["depth"] = new IntegerParameter(new Long(value)),
        };
    }

    private PureQLProjection Same(
        PureQLQuery query,
        IReadOnlyDictionary<string, IParameter>? parameters = null
    )
    {
        PureQLProjection actual = new(
            _database.Rewriting,
            query,
            parameters ?? new Dictionary<string, IParameter>()
        );
        IReadOnlyList<string> result = ResultText.Of(actual, true);

        Assert.Equal(
            ResultText.Of(
                new PureQLProjection(
                    PostgreSqlDatabase.InMemory,
                    query,
                    parameters ?? new Dictionary<string, IParameter>()
                ),
                true
            ),
            result
        );

        if (actual.Execution == QueryExecution.InMemory)
        {
            _ = Assert.IsType<NotSupportedException>(actual.FallbackReason);
        }

        return actual;
    }

    private void Fails(
        PureQLQuery query,
        IReadOnlyDictionary<string, IParameter>? parameters = null
    )
    {
        IReadOnlyDictionary<string, IParameter> bound =
            parameters ?? new Dictionary<string, IParameter>();

        _ = Assert.ThrowsAny<Exception>(() =>
            new PureQLProjection(PostgreSqlDatabase.InMemory, query, bound).ToList()
        );
        _ = Assert.ThrowsAny<Exception>(() =>
            new PureQLProjection(_database.Rewriting, query, bound).ToList()
        );
    }

    private static string Level(string field, string source = "walk")
    {
        return $$$"""{"source": "{{{source}}}", "field": "{{{field}}}", "type": {"name": "integer"}}""";
    }

    private static string Plus(string value, int step)
    {
        return $$$"""{"operator": "add", "type": {"name": "integer"}, "values": [{{{value}}}, {{{Literal("integer", step.ToString(CultureInfo.InvariantCulture))}}}]}""";
    }

    private static string Before(int last, string source = "walk")
    {
        return $$$"""{"operator": "lessThan", "type": {"name": "boolean"}, "left": {{{Level("depth", source)}}}, "right": {{{Literal("integer", last.ToString(CultureInfo.InvariantCulture))}}}}""";
    }

    // The readings of an id at or below the bound, at depth 0.
    private static string Seed(string bound, string? id = null)
    {
        return $$$"""
            {
              "from": {"entity": "t.readings"},
              "where": {"operator": "lessThanOrEqual", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Literal("integer", bound)}}}},
              "select": [
                {"alias": "depth", "expression": {{{Literal("integer", "0")}}}},
                {"alias": "id", "expression": {{{id ?? Field("id")}}}}
              ]
            }
            """;
    }

    private static string Next(string source, string where, string id)
    {
        return $$$"""
            {
              "from": {"subquery": "{{{source}}}"},
              "where": {{{where}}},
              "select": [
                {"alias": "depth", "expression": {{{Plus(Level("depth", source), 1)}}}},
                {"alias": "id", "expression": {{{id}}}}
              ]
            }
            """;
    }

    private static string Recursive(
        string name,
        string maxDepth,
        string seed,
        string next
    )
    {
        return $$$"""
            {
              "name": "{{{name}}}",
              "recursive": {"seed": {{{seed}}}, "next": {{{next}}}, "maxDepth": {{{maxDepth}}}}
            }
            """;
    }

    // Every row of walk, by depth and id.
    private static PureQLQuery WalkOf(string subqueries)
    {
        return Query(
            $$$"""
            {
              "subqueries": [{{{subqueries}}}],
              "from": {"subquery": "walk"},
              "select": [
                {"alias": "depth", "expression": {{{Level("depth")}}}},
                {"alias": "id", "expression": {{{Level("id")}}}}
              ],
              "orderBy": [{"expression": {{{Level("depth")}}}}, {"expression": {{{Level(
                "id"
            )}}}}]
            }
            """
        );
    }

    private static PureQLQuery Walk(
        string maxDepth,
        string where,
        string? id = null,
        string? seedId = null,
        string seedBound = "1"
    )
    {
        return WalkOf(
            Recursive(
                "walk",
                maxDepth,
                Seed(seedBound, seedId),
                Next("walk", where, id ?? Plus(Level("id"), 1))
            )
        );
    }

    // Sets the maxDepth of the query's recursion, as a model built in code
    // may hold it.
    private static void SetMaxDepth(PureQLQuery query, long maxDepth)
    {
        ModelRecursion recursion =
            Find(query, [])
            ?? throw new InvalidOperationException("The query has no recursion.");

        typeof(ModelRecursion)
            .GetField(
                "<MaxDepth>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(recursion, new RecursionMaxDepth(maxDepth));
    }

    private static ModelRecursion? Find(object? node, List<object> seen)
    {
        if (
            node is null
            || node is string
            || node.GetType().IsPrimitive
            || seen.Any(item => ReferenceEquals(item, node))
        )
        {
            return null;
        }

        seen.Add(node);

        if (node is ModelRecursion recursion)
        {
            return recursion;
        }

        if (node is IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (Find(item, seen) is ModelRecursion found)
                {
                    return found;
                }
            }

            return null;
        }

        if (
            node.GetType().Namespace?.StartsWith("PureQL", StringComparison.Ordinal)
            != true
        )
        {
            return null;
        }

        foreach (
            PropertyInfo property in node.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0)
        )
        {
            if (Find(property.GetValue(node), seen) is ModelRecursion found)
            {
                return found;
            }
        }

        return null;
    }
}
