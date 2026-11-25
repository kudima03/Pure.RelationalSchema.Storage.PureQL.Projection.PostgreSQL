using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using PureQL.CSharp.Model;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// Recursive subqueries as WITH RECURSIVE: every level up to the first empty
// one, duplicates included, and a row deeper than maxDepth an error, as in
// memory. Needs Docker.
public sealed class RecursionTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(3, 10)]
    [InlineData(20, 20)]
    public void LevelsUpToTheFirstEmptyOneAreReturned(int last, int maxDepth)
    {
        IReadOnlyList<string> rows = Translation.Translated(
            _database,
            Walk(last, maxDepth, "1")
        );

        Assert.Equal(last + 2, rows.Count);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(30, 20)]
    public void RowDeeperThanMaxDepthFailsTheQuery(int last, int maxDepth)
    {
        Translation.Fails(_database, Walk(last, maxDepth, "1"));
    }

    [Fact]
    public void LevelsKeepTheirDuplicates()
    {
        IReadOnlyList<string> rows = Translation.Translated(_database, Walk(2, 5, "10"));

        Assert.Equal(1 + (3 * 10), rows.Count);
    }

    // The ids of the readings at or below a bound, then each id one more
    // than the row before, to the last level; each row with its level.
    private static PureQLQuery Walk(int last, int maxDepth, string seedIds)
    {
        string depth = /*lang=json,strict*/
            """{"source": "walk", "field": "depth", "type": {"name": "integer"}}""";
        string id = /*lang=json,strict*/
            """{"source": "walk", "field": "id", "type": {"name": "integer"}}""";

        return Query(
            $$$"""
            {
              "subqueries": [
                {
                  "name": "walk",
                  "recursive": {
                    "seed": {
                      "from": {"entity": "t.readings"},
                      "where": {"operator": "lessThanOrEqual", "type": {"name": "boolean"}, "left": {{{Field(
                "id"
            )}}}, "right": {{{Literal("integer", seedIds)}}}},
                      "select": [
                        {"alias": "depth", "expression": {{{Literal("integer", "0")}}}},
                        {"alias": "id", "expression": {{{Field("id")}}}}
                      ]
                    },
                    "next": {
                      "from": {"subquery": "walk"},
                      "where": {"operator": "lessThan", "type": {"name": "boolean"}, "left": {{{depth}}}, "right": {{{Literal(
                "integer",
                last.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )}}}},
                      "select": [
                        {"alias": "depth", "expression": {"operator": "add", "type": {"name": "integer"}, "values": [{{{depth}}}, {{{Literal(
                "integer",
                "1"
            )}}}]}},
                        {"alias": "id", "expression": {"operator": "add", "type": {"name": "integer"}, "values": [{{{id}}}, {{{Literal(
                "integer",
                "1"
            )}}}]}}
                      ]
                    },
                    "maxDepth": {{{maxDepth}}}
                  }
                }
              ],
              "from": {"subquery": "walk"},
              "select": [
                {"alias": "depth", "expression": {{{depth}}}},
                {"alias": "id", "expression": {{{id}}}}
              ],
              "orderBy": [{"expression": {{{depth}}}}, {"expression": {{{id}}}}]
            }
            """
        );
    }
}
