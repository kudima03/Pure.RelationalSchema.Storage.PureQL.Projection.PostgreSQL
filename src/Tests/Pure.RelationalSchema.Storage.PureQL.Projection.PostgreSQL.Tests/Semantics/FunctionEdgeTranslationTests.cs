using System.Globalization;
using Pure.Primitives.Number;
using Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Parameter;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;
using static Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data.Json;
using Decimal = Pure.Primitives.Number.Decimal;
using IParameter = Pure.RelationalSchema.Storage.PureQL.Projection.Parameters.Abstractions.Parameter.IParameter;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Semantics;

// The edges of the rewritten text, conversion and number functions over the
// readings: operands that only the rows make values of PostgreSQL (a value
// chosen in each row by a condition on its id, so neither the interpreter nor
// the translator computes it beforehand), operands that are query parameters,
// digits at the bounds of a decimal and of a 64-bit integer, midpoints, signs,
// zero divisors and overflows, each translated with exactly the interpreter's
// result in memory, or failing in both, or left in memory. Needs Docker.
public sealed class FunctionEdgeTranslationTests(PostgreSqlDatabase database)
    : IClassFixture<PostgreSqlDatabase>
{
    private const string MaxDecimal = "\"9999999999999999999999999999\"";

    private readonly PostgreSqlDatabase _database = database;

    public static TheoryData<string> Strings =>
        [
            Op("length", "integer", ("operand", Row("string", "\"a😀b\""))),
            Op("length", "integer", ("operand", Row("string", "\"😀😀\""))),
            Op("length", "integer", ("operand", Row("string", "\"\""))),
            Op("length", "integer", ("operand", Row("string", "\"e\\u0301\""))),
            Op("length", "integer", ("operand", Row("string", "\"\\u00AD\""))),
            Position(Row("string", "\"a😀b\""), Row("string", "\"b\"")),
            Position(Row("string", "\"😀😀x😀\""), Row("string", "\"x😀\"")),
            Position(Row("string", "\"\""), Row("string", "\"\"")),
            Position(Row("string", "\"\""), Row("string", "\"a\"")),
            Position(Row("string", "\"ab\""), Row("string", "\"abc\"")),
            Position(Row("string", "\"ABC\""), Row("string", "\"b\"")),
            Position(Row("string", "\"Straße\""), Row("string", "\"SS\"")),
            Position(Row("string", "\"strasse\""), Row("string", "\"ß\"")),
            Position(Row("string", "\"e\\u0301\""), Row("string", "\"e\"")),
            Position(Row("string", "\"\\u00C4\""), Row("string", "\"A\\u0308\"")),
            Position(Row("string", "\"A\\u0308\""), Row("string", "\"\\u00C4\"")),
            Position(Row("string", "\"aXbXc\""), Row("string", "\"X\"")),
            Position(Field("label"), Row("string", "\"\\u00AD\"")),
            Position(Field("label"), Field("label")),
            Replace(Row("string", "\"aaa\""), Row("string", "\"aa\""), Text("b")),
            Replace(Row("string", "\"aaaa\""), Row("string", "\"aa\""), Text("a")),
            Replace(Row("string", "\"abab\""), Row("string", "\"ab\""), Text("abab")),
            Replace(Row("string", "\"\""), Row("string", "\"\""), Text("x")),
            Replace(Row("string", "\"\""), Row("string", "\"a\""), Text("x")),
            Replace(Row("string", "\"abc\""), Row("string", "\"abcd\""), Text("x")),
            Replace(Row("string", "\"a😀b😀\""), Row("string", "\"😀\""), Text("")),
            Replace(
                Row("string", "\"\\u00C4A\\u0308\""),
                Row("string", "\"A\\u0308\""),
                Text("\\u00C4")
            ),
            Replace(Row("string", "\"Apple\""), Row("string", "\"apple\""), Text("x")),
            Replace(Field("label"), Field("label"), Text("")),
            Replace(Field("label"), Text("p"), Field("label")),
        ];

    [Theory]
    [MemberData(nameof(Strings))]
    public void TextFunctionIsComputedByPostgreSqlAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    public static TheoryData<string> Conversions =>
        [
            AsString(Row("integer", "-9223372036854775808")),
            AsString(Row("integer", "9223372036854775807")),
            AsString(Row("integer", "0")),
            AsString(Row("integer", "-1")),
            AsString(Row("uuid", "\"ffffffff-ffff-ffff-ffff-ffffffffffff\"")),
            AsString(Row("uuid", "\"00000000-0000-0000-0000-000000000000\"")),
        ];

    [Theory]
    [MemberData(nameof(Conversions))]
    public void ConversionIsComputedByPostgreSqlAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    public static TheoryData<string> Arithmetic =>
        [
            Modulo("integer", Row("integer", "-9223372036854775808"), Int("-1")),
            Modulo("integer", Row("integer", "-9223372036854775808"), Int("1")),
            Modulo("integer", Row("integer", "-9223372036854775808"), Int("10")),
            Modulo("integer", Row("integer", "-7"), Int("3")),
            Modulo("integer", Row("integer", "7"), Int("-3")),
            Modulo("integer", Row("integer", "-7"), Int("-3")),
            Modulo("decimal", Row("decimal", "\"2.75\""), Dec("0.25")),
            Modulo("decimal", Row("decimal", "\"10\""), Dec("0.3")),
            Modulo("decimal", Row("decimal", "\"-10\""), Dec("0.3")),
            Modulo("decimal", Row("decimal", "\"10\""), Dec("-0.3")),
            Modulo(
                "decimal",
                Row("decimal", "\"0.0000000000000000000000000001\""),
                Int("1")
            ),
            Modulo(
                "decimal",
                Row("decimal", MaxDecimal),
                Dec("0.0000000000000000000000000007")
            ),
            Modulo("decimal", Row("decimal", MaxDecimal), Dec("-7.9")),
            Modulo(
                "decimal",
                Row("decimal", "\"-9999999999999999999999999999\""),
                Int("2")
            ),
            Modulo("decimal", Field("amount"), Dec("0.125")),
            IntegerDivide(Row("decimal", "\"-7.5\""), Int("2")),
            IntegerDivide(Row("decimal", "\"7.5\""), Int("-2")),
            IntegerDivide(Row("decimal", "\"-7.5\""), Int("-2")),
            IntegerDivide(Row("decimal", "\"0.9\""), Int("1")),
            IntegerDivide(Row("decimal", "\"-0.9\""), Int("1")),
            IntegerDivide(Row("decimal", "\"9223372036854775807.9\""), Int("1")),
            IntegerDivide(Row("decimal", "\"-9223372036854775808.9\""), Int("1")),
            IntegerDivide(
                Row("decimal", "\"0.0000000000000000000000000003\""),
                Dec("0.0000000000000000000000000001")
            ),
            IntegerDivide(
                Row("decimal", "\"12345678901234567.8901234567\""),
                Dec("0.1234567890123456789")
            ),
            IntegerDivide(Row("decimal", "\"1\""), Dec("0.3")),
            IntegerDivide(Row("decimal", MaxDecimal), Dec("8589934592")),
            IntegerDivide(Field("amount"), Dec("0.3")),
        ];

    [Theory]
    [MemberData(nameof(Arithmetic))]
    public void ArithmeticIsComputedByPostgreSqlAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    public static TheoryData<string> Rounding =>
        [
            Round("integer", Row("integer", "25"), "-1", "halfEven"),
            Round("integer", Row("integer", "35"), "-1", "halfEven"),
            Round("integer", Row("integer", "-25"), "-1", "halfEven"),
            Round("integer", Row("integer", "-15"), "-1", "halfEven"),
            Round("integer", Row("integer", "-25"), "-1", null),
            Round("integer", Row("integer", "-24"), "-1", null),
            Round("integer", Row("integer", "4999999999999999999"), "-19", null),
            Round("integer", Row("integer", "-5000000000000000000"), "-19", "halfEven"),
            Round("integer", Row("integer", "5000000000000000000"), "-19", "halfEven"),
            Round("integer", Row("integer", "9223372036854775807"), "-20", null),
            Round("integer", Row("integer", "-9223372036854775808"), "-28", null),
            Round("integer", Row("integer", "9223372036854775807"), "-18", null),
            Round("integer", Row("integer", "-9223372036854775808"), "-18", "halfEven"),
            Round("integer", Row("integer", "9223372036854775807"), "0", null),
            Round("integer", Field("n"), "9223372036854775807", null),
            Round("integer", Field("n"), "-1", "halfEven"),
            Round("decimal", Row("decimal", "\"2.5\""), "0", null),
            Round("decimal", Row("decimal", "\"2.5\""), "0", "halfEven"),
            Round("decimal", Row("decimal", "\"-2.5\""), "0", null),
            Round("decimal", Row("decimal", "\"-2.5\""), "0", "halfEven"),
            Round("decimal", Row("decimal", "\"-3.5\""), "0", "halfEven"),
            Round("decimal", Row("decimal", "\"0.125\""), "2", "halfEven"),
            Round("decimal", Row("decimal", "\"0.135\""), "2", "halfEven"),
            Round("decimal", Row("decimal", "\"-0.125\""), "2", null),
            Round(
                "decimal",
                Row("decimal", "\"1.000000000000000000000000005\""),
                "26",
                null
            ),
            Round(
                "decimal",
                Row("decimal", "\"1.000000000000000000000000005\""),
                "26",
                "halfEven"
            ),
            Round(
                "decimal",
                Row("decimal", "\"1.000000000000000000000000015\""),
                "26",
                "halfEven"
            ),
            Round(
                "decimal",
                Row("decimal", "\"-1.000000000000000000000000015\""),
                "26",
                "halfEven"
            ),
            Round(
                "decimal",
                Row("decimal", "\"1.000000000000000000000000005\""),
                "27",
                null
            ),
            Round(
                "decimal",
                Row("decimal", "\"0.0000000000000000000000000005\""),
                "27",
                null
            ),
            Round(
                "decimal",
                Row("decimal", "\"0.0000000000000000000000000005\""),
                "27",
                "halfEven"
            ),
            Round("decimal", Row("decimal", "\"1.5\""), "9223372036854775807", null),
            Round(
                "decimal",
                Row("decimal", "\"5000000000000000000000000000\""),
                "-27",
                null
            ),
            Round(
                "decimal",
                Row("decimal", "\"4500000000000000000000000000\""),
                "-27",
                "halfEven"
            ),
            Round("decimal", Row("decimal", MaxDecimal), "-29", null),
            Round("decimal", Row("decimal", MaxDecimal), "-9223372036854775808", null),
            Round("decimal", Row("decimal", "\"-250\""), "0", null),
            Round("decimal", Row("decimal", "\"-250.5\""), "-2", null),
            Truncate("integer", Row("integer", "-9223372036854775808"), "-18"),
            Truncate("integer", Row("integer", "9223372036854775807"), "-18"),
            Truncate("integer", Row("integer", "-19"), "-1"),
            Truncate("integer", Row("integer", "19"), "-1"),
            Truncate("integer", Row("integer", "-9223372036854775808"), "-1"),
            Truncate("integer", Field("n"), "9223372036854775807"),
            Truncate("decimal", Row("decimal", "\"-7.59\""), "1"),
            Truncate("decimal", Row("decimal", "\"7.59\""), "0"),
            Truncate("decimal", Row("decimal", "\"-0.5\""), "0"),
            Truncate("decimal", Row("decimal", "\"-0.05\""), "1"),
            Truncate("decimal", Row("decimal", MaxDecimal), "-28"),
            Truncate(
                "decimal",
                Row("decimal", "\"-9999999999999999999999999999\""),
                "-27"
            ),
            Truncate("decimal", Row("decimal", MaxDecimal), "-1"),
            Truncate(
                "decimal",
                Row("decimal", "\"0.0000000000000000000000000019\""),
                "27"
            ),
            Truncate(
                "decimal",
                Row("decimal", "\"-0.0000000000000000000000000019\""),
                "27"
            ),
            Truncate(
                "decimal",
                Row("decimal", "\"0.0000000000000000000000000019\""),
                "28"
            ),
            Truncate("decimal", Row("decimal", "\"1.5\""), "9223372036854775807"),
            Truncate("decimal", Row("decimal", "\"-155.5\""), "-2"),
            Truncate("decimal", Field("amount"), "2"),
            Truncate("decimal", Field("amount"), "-2"),
            Truncate("decimal", Row("decimal", "\"-250\""), "-2"),
            Truncate("decimal", Row("decimal", "\"-7\""), "0"),
            Modulo("decimal", Row("decimal", "\"-7\""), Int("2")),
            IntegerDivide(Row("decimal", "\"-7\""), Int("2")),
            IntegerDivide(Row("decimal", "\"7\""), Dec("-2")),
        ];

    [Theory]
    [MemberData(nameof(Rounding))]
    public void RoundingIsComputedByPostgreSqlAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    public static TheoryData<string> Failing =>
        [
            Modulo("decimal", Field("amount"), Dec("0")),
            Modulo("decimal", Field("amount"), Field("amount")),
            IntegerDivide(Field("amount"), Field("amount")),
            IntegerDivide(Field("amount"), Dec("0")),
            IntegerDivide(Row("decimal", "\"9223372036854775808\""), Int("1")),
            IntegerDivide(Row("decimal", "\"-9223372036854775809\""), Int("1")),
            IntegerDivide(Row("decimal", "\"1\""), Dec("0.0000000000000000000000000003")),
            Round("integer", Row("integer", "9223372036854775807"), "-1", null),
            Round("integer", Row("integer", "-9223372036854775808"), "-1", "halfEven"),
            Round("integer", Row("integer", "5000000000000000000"), "-19", null),
            Round("integer", Row("integer", "-5000000000000000001"), "-19", "halfEven"),
        ];

    [Theory]
    [MemberData(nameof(Failing))]
    public void FunctionThatFailsInMemoryFailsInPostgreSql(string expression)
    {
        _ = Assert.ThrowsAny<Exception>(() =>
            ResultText.Of(
                new PureQLProjection(PostgreSqlDatabase.InMemory, Valued(expression)),
                true
            )
        );
        _ = Assert.ThrowsAny<Exception>(() =>
            ResultText.Of(
                new PureQLProjection(_database.Rewriting, Valued(expression)),
                true
            )
        );
    }

    public static TheoryData<string> WholeDecimals =>
        [
            Round("decimal", Row("decimal", "\"-250\""), "-2", null),
            Round("decimal", Row("decimal", "\"250\""), "-2", null),
            Round("decimal", Row("decimal", "\"-250\""), "-2", "halfEven"),
            Round("decimal", Row("decimal", "\"-150\""), "-2", "halfEven"),
        ];

    // A decimal that is a whole number is written by the translator as an
    // integer literal, so the interpreter's own shape for negative digits,
    // Math.Round(x / 100, mode) * 100, divides integers in PostgreSQL:
    // round(-250, -2) is translated -200 (in memory -300), and half to even
    // fails the statement ("function mod(double precision, integer) does not
    // exist", TRUNC of an integer being a double). Not a shape this package
    // writes: the interpreter's tree and the translator's literal.
    [Theory(
        Skip = "Bug (upstream translator): a whole decimal is written as an integer literal, so round to negative digits divides integers"
    )]
    [MemberData(nameof(WholeDecimals))]
    public void RoundOfAWholeDecimalToNegativeDigitsIsAsInMemory(string expression)
    {
        _ = Translation.Translated(_database, Valued(expression));
    }

    public static TheoryData<string> OutOfRange =>
        [
            Round("decimal", Row("decimal", MaxDecimal), "-28", null),
            Round("decimal", Row("decimal", MaxDecimal), "-27", "halfEven"),
        ];

    // round(9999999999999999999999999999, -28) is 10^28, outside the
    // specification's decimals: in memory it fails (OverflowException "A
    // decimal of magnitude 10^28 or more"); translated, PostgreSQL's numeric
    // has no such bound and the row reads 10000000000000000000000000000. The
    // shape is the interpreter's own Math.Round, not one this package writes.
    [Theory(
        Skip = "Bug (upstream translator): a rounded decimal of 10^28 is returned by PostgreSQL where the specification fails"
    )]
    [MemberData(nameof(OutOfRange))]
    public void RoundOutOfTheRangeOfADecimalFailsInPostgreSql(string expression)
    {
        FunctionThatFailsInMemoryFailsInPostgreSql(expression);
    }

    public static TheoryData<string> Kept =>
        [
            Round("integer", Field("n"), "-29", null),
            Round("integer", Field("n"), "-9223372036854775808", null),
            Truncate("integer", Field("n"), "-19"),
            Truncate("integer", Field("n"), "-9223372036854775808"),
            Truncate("decimal", Field("amount"), "-29"),
            Truncate("decimal", Field("amount"), "-9223372036854775808"),
            Op(
                "round",
                "integer",
                ("operand", Field("n")),
                (
                    "digits",
                    Op(
                        "subtract",
                        "integer",
                        ("values", "[" + Field("n") + ", " + Field("n") + "]")
                    )
                )
            ),
            Op(
                "truncate",
                "integer",
                ("operand", Field("n")),
                (
                    "digits",
                    Op(
                        "subtract",
                        "integer",
                        ("values", "[" + Field("n") + ", " + Field("n") + "]")
                    )
                )
            ),
            Op(
                "position",
                "integer",
                ("operand", Field("label")),
                ("pattern", Text("a")),
                ("start", Literal("integer", "2"))
            ),
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void FunctionPostgreSqlCannotTakeRunsInMemory(string expression)
    {
        _ = Translation.Refused(_database, Valued(expression));
    }

    public static TheoryData<string, long> DigitsParameters =>
        new()
        {
            { Round("integer", Field("n"), Param("d"), null), -1 },
            { Round("integer", Field("n"), Param("d"), "halfEven"), -1 },
            { Round("integer", Field("n"), Param("d"), null), 5 },
            { Round("decimal", Field("amount"), Param("d"), null), 1 },
            { Round("decimal", Field("amount"), Param("d"), "halfEven"), 1 },
            { Round("decimal", Field("amount"), Param("d"), "halfEven"), 0 },
            { Truncate("integer", Field("n"), Param("d")), -1 },
            { Truncate("decimal", Field("amount"), Param("d")), 1 },
            { Truncate("decimal", Field("amount"), Param("d")), -1 },
            {
                Round(
                    "integer",
                    Field("n"),
                    Op(
                        "add",
                        "integer",
                        ("values", "[" + Param("d") + ", " + Int("-2") + "]")
                    ),
                    null
                ),
                1
            },
        };

    [Theory]
    [MemberData(nameof(DigitsParameters))]
    public void DigitsOfAParameterAreTranslatedAsInMemory(string expression, long digits)
    {
        Dictionary<string, IParameter> parameters = new()
        {
            ["d"] = new IntegerParameter(new Long(digits)),
        };

        Assert.Equal(
            QueryExecution.Translated,
            SameWith(expression, parameters).Execution
        );
    }

    // A null digits parameter makes the whole result null in memory. The
    // rewriter reads the parameter's value to choose the shape and throws
    // InvalidOperationException ("Cannot read the Value property of a
    // Nullable object that has no value") out of the provider. Observed: the
    // query fails; expected: null in every row.
    [Fact(
        Skip = "Bug: a null digits parameter of round (integer) throws InvalidOperationException instead of giving null"
    )]
    public void RoundOfAnIntegerToNullDigitsIsNullAsInMemory()
    {
        _ = SameWith(
            Round("integer", Field("n"), NullableParam("d"), null),
            NullDigits()
        );
    }

    [Fact(
        Skip = "Bug: a null digits parameter of truncate (integer) throws InvalidOperationException instead of giving null"
    )]
    public void TruncateOfAnIntegerToNullDigitsIsNullAsInMemory()
    {
        _ = SameWith(Truncate("integer", Field("n"), NullableParam("d")), NullDigits());
    }

    [Fact(
        Skip = "Bug: a null digits parameter of truncate (decimal) throws InvalidOperationException instead of giving null"
    )]
    public void TruncateOfADecimalToNullDigitsIsNullAsInMemory()
    {
        _ = SameWith(
            Truncate("decimal", Field("amount"), NullableParam("d")),
            NullDigits()
        );
    }

    // The interpreter's own shape for a decimal (Math.Round over digits read
    // from @d, not a call this package rewrites) is translated with the null
    // parameter written untyped: "operator does not exist: text >= integer".
    [Fact(
        Skip = "Bug (upstream translator): a null digits parameter of round (decimal) fails the statement (text >= integer)"
    )]
    public void RoundOfADecimalToNullDigitsIsNullAsInMemory()
    {
        _ = SameWith(
            Round("decimal", Field("amount"), NullableParam("d"), null),
            NullDigits()
        );
    }

    public static TheoryData<string> OfParameters =>
        [
            Op("length", "integer", ("operand", Param("s", "string"))),
            Position(Param("s", "string"), Param("p", "string")),
            Replace(Param("s", "string"), Param("p", "string"), Text("-")),
            AsString(Param("i")),
            AsString(Param("u", "uuid")),
            Modulo("decimal", Param("x", "decimal"), Param("y", "decimal")),
            IntegerDivide(Param("x", "decimal"), Param("y", "decimal")),
            Round("integer", Param("i"), "-1", "halfEven"),
            Truncate("integer", Param("i"), "-1"),
            Truncate("decimal", Param("x", "decimal"), "1"),
        ];

    // Operands that are parameters, with values that do not reach an edge:
    // whoever computes the call computes it as the specification does.
    [Theory]
    [MemberData(nameof(OfParameters))]
    public void FunctionOfParametersIsComputedAsInMemory(string expression)
    {
        _ = SameWith(expression, Parameters("abcb", "b", 1.25m, 0.5m));
    }

    // The operand is a parameter, so the translator computes the rewritten
    // shape in .NET before it writes the statement: string.Length counts
    // UTF-16 units. Translated: 4 in every row; in memory: 3 (code points).
    [Fact(
        Skip = "Bug: length of a parameter is computed by .NET (UTF-16 units, 4) instead of code points (3)"
    )]
    public void LengthOfAParameterOutsideTheBasicPlaneCountsCodePoints()
    {
        _ = SameWith(
            Op("length", "integer", ("operand", Param("s", "string"))),
            Parameters("a😀b", "b", 1m, 1m)
        );
    }

    // The operands are parameters, so the translator computes IndexOf + 1 in
    // .NET: a UTF-16 index. Translated: 4; in memory: 3 (code points).
    [Fact(
        Skip = "Bug: position over parameters is computed by .NET IndexOf (UTF-16 index, 4) instead of code points (3)"
    )]
    public void PositionOfParametersOutsideTheBasicPlaneCountsCodePoints()
    {
        _ = SameWith(
            Position(Param("s", "string"), Param("p", "string")),
            Parameters("a😀b", "b", 1m, 1m)
        );
    }

    // The operands are parameters, so the translator computes IndexOf in .NET
    // with the current culture's linguistic comparison, which ignores a soft
    // hyphen: translated 1; in memory 0 (the code point is not in "abc").
    [Fact(
        Skip = "Bug: position over parameters is computed by culture-sensitive .NET IndexOf (soft hyphen found at 1, expected 0)"
    )]
    public void PositionOfParametersMatchesCodePointsNotCollation()
    {
        _ = SameWith(
            Position(Param("s", "string"), Param("p", "string")),
            Parameters("abc", "\u00AD", 1m, 1m)
        );
    }

    [Fact]
    public void PositionOfParametersWithAnEmptyPatternIsOne()
    {
        _ = SameWith(
            Position(Param("s", "string"), Param("p", "string")),
            Parameters("abc", "", 1m, 1m)
        );
    }

    [Fact]
    public void ReplaceOfParametersWithAnEmptyPatternLeavesTheOperand()
    {
        _ = SameWith(
            Replace(Param("s", "string"), Param("p", "string"), Text("-")),
            Parameters("abc", "", 1m, 1m)
        );
    }

    [Fact]
    public void ReplaceOfParametersWithOverlappingOccurrencesIsAsInMemory()
    {
        _ = SameWith(
            Replace(Param("s", "string"), Param("p", "string"), Text("b")),
            Parameters("aaaaa", "aa", 1m, 1m)
        );
    }

    // The operand is a parameter, so the translator computes long.ToString()
    // in .NET with the current culture: under sv-SE the minus sign is U+2212.
    // Translated: "−12"; in memory: "-12".
    [Fact(
        Skip = "Bug: asString of an integer parameter is computed by .NET ToString() in the current culture (sv-SE: U+2212 minus)"
    )]
    public void AsStringOfANegativeIntegerParameterIsItsLiteralInAnyCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            _ = SameWith(AsString(Param("i")), Parameters("a", "b", 1m, 1m));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void ModuloOfDecimalParametersAtTheEdgeOfADecimalIsExact()
    {
        _ = SameWith(
            Modulo("decimal", Param("x", "decimal"), Param("y", "decimal")),
            Parameters(
                "a",
                "b",
                9999999999999999999999999999m,
                0.0000000000000000000000000007m
            )
        );
    }

    [Fact]
    public void IntegerDivideOfDecimalParametersWithManyDigitsIsExact()
    {
        _ = SameWith(
            IntegerDivide(Param("x", "decimal"), Param("y", "decimal")),
            Parameters("a", "b", 1234567890123456789m, 1.234567890123456789012345678m)
        );
    }

    // A midpoint by less than the last place PostgreSQL's numeric division
    // keeps: round(250.0000000000000000000000001, -2, halfEven) is 300 in
    // memory; the interpreter's tree divides by 100 (numeric /, rounded to
    // the operand's scale), so PostgreSQL sees 2.5, rounds it to even and
    // gives 200. The division is the interpreter's and the translator's (the
    // README's known gap), not a shape of this package.
    [Fact(
        Skip = "Bug (upstream): round to negative digits divides in numeric /, translated 200 instead of 300"
    )]
    public void RoundToNegativeDigitsJustAboveAMidpointIsExact()
    {
        _ = Translation.Translated(
            _database,
            Valued(
                Round(
                    "decimal",
                    Row("decimal", "\"250.0000000000000000000000001\""),
                    "-2",
                    "halfEven"
                )
            )
        );
    }

    private PureQLProjection SameWith(
        string expression,
        IReadOnlyDictionary<string, IParameter> parameters
    )
    {
        PureQLProjection translated = new(
            _database.Rewriting,
            Valued(expression),
            parameters
        );
        PureQLProjection memory = new(
            PostgreSqlDatabase.InMemory,
            Valued(expression),
            parameters
        );

        Assert.Equal(ResultText.Of(memory, true), ResultText.Of(translated, true));

        return translated;
    }

    private static Dictionary<string, IParameter> Parameters(
        string text,
        string pattern,
        decimal left,
        decimal right
    )
    {
        return new Dictionary<string, IParameter>
        {
            ["s"] = new StringParameter(new String(text)),
            ["p"] = new StringParameter(new String(pattern)),
            ["i"] = new IntegerParameter(new Long(-12)),
            ["u"] = new UuidParameter(
                new Primitives.Guid.Guid(new Guid("0000000A-0000-0000-0000-00000000000B"))
            ),
            ["x"] = new DecimalParameter(new Decimal(left)),
            ["y"] = new DecimalParameter(new Decimal(right)),
        };
    }

    private static Dictionary<string, IParameter> NullDigits()
    {
        return new Dictionary<string, IParameter>
        {
            ["d"] = new NullableIntegerParameter(),
        };
    }

    // A value of the type in every row: the condition holds for each id, so
    // PostgreSQL computes the function over it, the row's value.
    private static string Row(string type, string value)
    {
        string other = type switch
        {
            "string" => "\"\"",
            "uuid" => "\"00000000-0000-0000-0000-000000000001\"",
            "decimal" => "\"0\"",
            _ => "0",
        };

        return "{\"operator\": \"if\", \"type\": "
            + Type(type)
            + ", \"condition\": {\"operator\": \"greaterThan\", \"type\": {\"name\": \"boolean\"}, \"left\": "
            + Field("id")
            + ", \"right\": "
            + Literal("integer", "0")
            + "}, \"then\": "
            + Literal(type, value)
            + ", \"else\": "
            + Literal(type, other)
            + "}";
    }

    private static string Param(string name, string type = "integer")
    {
        return "{\"param\": \"" + name + "\", \"type\": " + Type(type) + "}";
    }

    private static string NullableParam(string name)
    {
        return "{\"param\": \"" + name + "\", \"type\": " + Type("integer", true) + "}";
    }

    private static string Text(string value)
    {
        return Literal("string", "\"" + value + "\"");
    }

    private static string Int(string value)
    {
        return Literal("integer", value);
    }

    private static string Dec(string value)
    {
        return Literal("decimal", "\"" + value + "\"");
    }

    private static string Position(string operand, string pattern)
    {
        return Op("position", "integer", ("operand", operand), ("pattern", pattern));
    }

    private static string Replace(string operand, string pattern, string replacement)
    {
        return Op(
            "replace",
            "string",
            ("operand", operand),
            ("pattern", pattern),
            ("replacement", replacement)
        );
    }

    private static string AsString(string operand)
    {
        return Op("asString", "string", ("operand", operand));
    }

    private static string Modulo(string type, string left, string right)
    {
        return Op("modulo", type, ("left", left), ("right", right));
    }

    private static string IntegerDivide(string left, string right)
    {
        return Op("integerDivide", "integer", ("left", left), ("right", right));
    }

    // round(operand, digits) half away from zero, or in the mode given; the
    // digits are a literal integer or an expression.
    private static string Round(string type, string operand, string digits, string? mode)
    {
        string places = digits.StartsWith('{') ? digits : Int(digits);

        return mode is null
            ? Op("round", type, ("operand", operand), ("digits", places))
            : Op(
                "round",
                type,
                ("operand", operand),
                ("digits", places),
                ("mode", "\"" + mode + "\"")
            );
    }

    private static string Truncate(string type, string operand, string digits)
    {
        string places = digits.StartsWith('{') ? digits : Int(digits);

        return Op("truncate", type, ("operand", operand), ("digits", places));
    }

    // The operator's declared type is nullable when one of its operands is.
    private static string Op(
        string name,
        string type,
        params (string Name, string Value)[] operands
    )
    {
        bool nullable = operands.Any(operand =>
            operand.Value.Contains("\"nullable\": true", StringComparison.Ordinal)
        );

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
