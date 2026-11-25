using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// Each rewritten function over every row of the readings (nulls, negative
// numbers, ties, the ends of the calendar, text outside ASCII and the Basic
// Multilingual Plane), translated with exactly the interpreter's results,
// and the functions left as they are, in memory. Needs Docker.
public sealed class FunctionTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private readonly PostgreSqlDatabase _database = database;

    public static TheoryData<string> Rewritten =>
        [
            Unary("length", "integer", false, "label"),
            Unary("length", "integer", true, "grp"),
            Op(
                "position",
                "integer",
                false,
                ("operand", Field("label")),
                ("pattern", Text("p"))
            ),
            Op(
                "position",
                "integer",
                false,
                ("operand", Field("label")),
                ("pattern", Text(""))
            ),
            Op(
                "position",
                "integer",
                false,
                ("operand", Field("label")),
                ("pattern", Text("x"))
            ),
            Op(
                "position",
                "integer",
                false,
                ("operand", Field("label")),
                ("pattern", Text("ß"))
            ),
            Op(
                "position",
                "integer",
                true,
                ("operand", Field("grp")),
                ("pattern", Field("grp"))
            ),
            Op(
                "replace",
                "string",
                false,
                ("operand", Field("label")),
                ("pattern", Text("a")),
                ("replacement", Text("-"))
            ),
            Op(
                "replace",
                "string",
                false,
                ("operand", Field("label")),
                ("pattern", Text("")),
                ("replacement", Text("-"))
            ),
            Op(
                "replace",
                "string",
                false,
                ("operand", Field("label")),
                ("pattern", Text("-b-")),
                ("replacement", Text("|"))
            ),
            Op(
                "replace",
                "string",
                false,
                ("operand", Field("label")),
                ("pattern", Text("😀")),
                ("replacement", Text("ß"))
            ),
            Unary("asString", "string", true, "n"),
            Unary("asString", "string", false, "id"),
            Unary("asString", "string", false, "uid"),
            Op(
                "modulo",
                "integer",
                true,
                ("left", Field("n")),
                ("right", Literal("integer", "4"))
            ),
            Op(
                "modulo",
                "integer",
                true,
                ("left", Field("n")),
                ("right", Literal("integer", "-1"))
            ),
            Op(
                "modulo",
                "decimal",
                true,
                ("left", Field("amount")),
                ("right", Literal("decimal", "\"0.5\""))
            ),
            Op(
                "modulo",
                "decimal",
                true,
                ("left", Field("amount")),
                ("right", Literal("integer", "-2"))
            ),
            Op(
                "modulo",
                "decimal",
                true,
                ("left", Field("amount")),
                ("right", Literal("decimal", "\"0.0000000000000000000000000003\""))
            ),
            Op(
                "integerDivide",
                "integer",
                true,
                ("left", Field("amount")),
                ("right", Literal("decimal", "\"0.5\""))
            ),
            Op(
                "integerDivide",
                "integer",
                true,
                ("left", Field("amount")),
                ("right", Literal("integer", "-2"))
            ),
            Op(
                "integerDivide",
                "integer",
                true,
                ("left", Field("n")),
                ("right", Literal("integer", "4"))
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "0"))
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "1"))
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "-1"))
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "-2"))
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "1")),
                ("mode", "\"halfEven\"")
            ),
            Op(
                "round",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "0")),
                ("mode", "\"halfEven\"")
            ),
            Op(
                "round",
                "integer",
                true,
                ("operand", Field("n")),
                ("digits", Literal("integer", "-1"))
            ),
            Op(
                "round",
                "integer",
                true,
                ("operand", Field("n")),
                ("digits", Literal("integer", "-1")),
                ("mode", "\"halfEven\"")
            ),
            Op(
                "round",
                "integer",
                true,
                ("operand", Field("n")),
                ("digits", Literal("integer", "2"))
            ),
            Op(
                "truncate",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "1"))
            ),
            Op(
                "truncate",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("digits", Literal("integer", "-1"))
            ),
            Op(
                "truncate",
                "integer",
                true,
                ("operand", Field("n")),
                ("digits", Literal("integer", "-1"))
            ),
            Unary("quarter", "integer", true, "day"),
            Unary("quarter", "integer", true, "at"),
            Unary("dayOfWeek", "integer", true, "day"),
            Unary("dayOfWeek", "integer", true, "at"),
            Unary("microsecond", "integer", true, "clock"),
            Unary("startOfYear", "date", true, "day"),
            Unary("startOfQuarter", "date", true, "day"),
            Unary("startOfMonth", "date", true, "day"),
            Unary("startOfWeek", "date", true, "day"),
            Unary("startOfHour", "time", true, "clock"),
            Unary("startOfMinute", "time", true, "clock"),
            Unary("startOfSecond", "time", true, "clock"),
            Unary("startOfYear", "datetime", true, "at"),
            Unary("startOfQuarter", "datetime", true, "at"),
            Unary("startOfMonth", "datetime", true, "at"),
            Unary("startOfWeek", "datetime", true, "at"),
            Unary("startOfDay", "datetime", true, "at"),
            Unary("startOfHour", "datetime", true, "at"),
            Unary("startOfMinute", "datetime", true, "at"),
            Unary("startOfSecond", "datetime", true, "at"),
        ];

    [Theory]
    [MemberData(nameof(Rewritten))]
    public void FunctionIsComputedByPostgreSqlAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    // Translated, a stored datetime is read without its fraction of a
    // second, also where this package rewrites nothing (a field selected as
    // it is): 2024-02-29T23:59:59.999999Z is read 2024-02-29T23:59:59Z.
    [Fact(
        Skip = "A stored datetime is read without its fraction of a second when the query is translated"
    )]
    public void MicrosecondOfADatetimeIsComputedByPostgreSqlAsInMemory()
    {
        _ = Translation.Translated(
            _database,
            Valued(Unary("microsecond", "integer", true, "at"))
        );
    }

    [Fact]
    public void FunctionInAConditionAndAKeyIsTranslated()
    {
        string month = Unary("startOfMonth", "date", true, "day");
        string length = Unary("length", "integer", false, "label");

        _ = Translation.Translated(
            _database,
            Query(
                "{\"from\": {\"entity\": \"t.readings\"}, \"where\": {\"operator\": \"greaterThan\", \"type\": {\"name\": \"boolean\"}, \"left\": "
                    + length
                    + ", \"right\": "
                    + Literal("integer", "1")
                    + "}, \"groupBy\": [{\"alias\": \"month\", \"expression\": "
                    + month
                    + "}], \"select\": [{\"alias\": \"month\", \"expression\": {\"key\": \"month\", \"type\": "
                    + Type("date", true)
                    + "}}, {\"alias\": \"count\", \"expression\": {\"operator\": \"count\", \"type\": {\"name\": \"integer\"}}}], \"orderBy\": [{\"expression\": {\"key\": \"month\", \"type\": "
                    + Type("date", true)
                    + "}}]}"
            )
        );
    }

    [Fact]
    public void ModuloByZeroFailsAsInMemory()
    {
        Translation.Fails(
            _database,
            Valued(
                Op(
                    "modulo",
                    "integer",
                    false,
                    ("left", Field("id")),
                    ("right", Literal("integer", "0"))
                )
            )
        );
    }

    public static TheoryData<string> Kept =>
        [
            Unary("upper", "string", false, "label"),
            Unary("lower", "string", false, "label"),
            Unary("foldCase", "string", false, "label"),
            Unary("asString", "string", true, "amount"),
            Unary("asString", "string", true, "day"),
            Op(
                "power",
                "decimal",
                true,
                ("operand", Field("amount")),
                ("exponent", Literal("integer", "2"))
            ),
            Unary("sqrt", "decimal", false, "id"),
            Unary("week", "integer", true, "day"),
            Op(
                "matchesRegex",
                "boolean",
                false,
                ("operand", Field("label")),
                ("pattern", Text("a+"))
            ),
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void FunctionLeftAsItIsRunsInMemory(string expression)
    {
        _ = Translation.Refused(_database, Valued(expression));
    }

    private static string Text(string value)
    {
        return "{\"type\": {\"name\": \"string\"}, \"value\": \"" + value + "\"}";
    }

    private static string Unary(string name, string type, bool nullable, string field)
    {
        return Op(name, type, nullable, ("operand", Field(field)));
    }

    private static string Op(
        string name,
        string type,
        bool nullable,
        params (string Name, string Value)[] operands
    )
    {
        return "{\"operator\": \""
            + name
            + "\", \"type\": "
            + Type(type, nullable)
            + string.Concat(
                operands.Select(operand => $", \"{operand.Name}\": {operand.Value}")
            )
            + "}";
    }
}
