using System.Text.Json;
using System.Text.Json.Serialization;
using PureQL.CSharp.Model;
using PureQL.CSharp.Model.Serialization;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// PureQL documents over t.readings written as JSON, read by
// PureQL.CSharp.Model.Serialization, which rejects a document the schema
// rejects (a declared type that is not exact included).
internal static class Json
{
    private static readonly JsonSerializerOptions Options = Created();

    public static PureQLQuery Query(string json)
    {
        return JsonSerializer.Deserialize<PureQLQuery>(json, Options);
    }

    // A field of the readings: id integer, grp string?, n integer?, amount
    // decimal?, day date?, at datetime?, clock time?, label string, uid uuid.
    public static string Field(string name)
    {
        string type = name switch
        {
            "id" => Type("integer"),
            "label" => Type("string"),
            "uid" => Type("uuid"),
            "grp" => Type("string", true),
            "n" => Type("integer", true),
            "amount" => Type("decimal", true),
            "day" => Type("date", true),
            "at" => Type("datetime", true),
            _ => Type("time", true),
        };

        return $$"""{"source": "t.readings", "field": "{{name}}", "type": {{type}}}""";
    }

    public static string Type(string name, bool nullable = false)
    {
        return nullable
            ? $$"""{"name": "{{name}}", "nullable": true}"""
            : $$"""{"name": "{{name}}"}""";
    }

    public static string Literal(string type, string value)
    {
        return $$"""{"type": {{Type(type)}}, "value": {{value}}}""";
    }

    // The readings in the order of id, each with its id and a value.
    public static PureQLQuery Valued(string value)
    {
        return Query(
            $$"""
            {
              "from": {"entity": "t.readings"},
              "select": [
                {"alias": "id", "expression": {{Field("id")}}},
                {"alias": "value", "expression": {{value}}}
              ],
              "orderBy": [{"expression": {{Field("id")}}}]
            }
            """
        );
    }

    private static JsonSerializerOptions Created()
    {
        JsonSerializerOptions options = new() { MaxDepth = 256 };

        foreach (JsonConverter converter in new PureQLConverters())
        {
            options.Converters.Add(converter);
        }

        return options;
    }
}
