using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The edges of the number and text rules, in and out: digits at the bounds
// of a decimal (28 places, 10^28) and of a 64-bit integer (10^18 the largest
// power of ten it holds), the extreme digits, digits computed from closed
// values, modes PostgreSQL has no form of, and functions in each other.
public sealed class FunctionEdgeTests
{
    private static readonly string[] Removables = [" "];

    private static readonly Holder Bound = new() { Digits = 2, Missing = null };

    public static TheoryData<Expression<Func<Values, object>>, string> Rewritten =>
        new()
        {
            {
                v => PureQLMath.Round(v.Integer, -28L, MidpointRounding.AwayFromZero),
                "(object)(long)(Math.Round(((decimal)v.Integer * 0.0000000000000000000000000001m), 0, MidpointRounding.AwayFromZero) * 10000000000000000000000000000m)"
            },
            {
                v => PureQLMath.Round(v.Integer, -19L, MidpointRounding.ToEven),
                "(object)(long)(Math.Round(((decimal)v.Integer * 0.0000000000000000001m), 0, MidpointRounding.ToEven) * 10000000000000000000m)"
            },
            {
                v => PureQLMath.Round(v.Integer, 0L, MidpointRounding.ToEven),
                "(object)v.Integer"
            },
            {
                v => PureQLMath.Round(v.Integer, long.MaxValue, MidpointRounding.ToEven),
                "(object)v.Integer"
            },
            {
                v => PureQLMath.Round(v.Decimal, 27L, MidpointRounding.ToEven),
                "(object)Math.Round(v.Decimal, 27, MidpointRounding.ToEven)"
            },
            {
                v => PureQLMath.Round(v.Decimal, 0L, MidpointRounding.AwayFromZero),
                "(object)Math.Round(v.Decimal, 0, MidpointRounding.AwayFromZero)"
            },
            {
                v => PureQLMath.Round(v.Decimal, -1L, MidpointRounding.ToEven),
                "(object)(Math.Round((v.Decimal * 0.1m), 0, MidpointRounding.ToEven) * 10m)"
            },
            {
                v => PureQLMath.Round(v.Decimal, -28L, MidpointRounding.AwayFromZero),
                "(object)(Math.Round((v.Decimal * 0.0000000000000000000000000001m), 0, MidpointRounding.AwayFromZero) * 10000000000000000000000000000m)"
            },
            {
                v => PureQLMath.Round(v.Decimal, long.MaxValue, MidpointRounding.ToEven),
                "(object)v.Decimal"
            },
            {
                v =>
                    PureQLMath.Round(
                        v.Integer,
                        Bound.Digits - 3L,
                        MidpointRounding.ToEven
                    ),
                "(object)(long)(Math.Round(((decimal)v.Integer * 0.1m), 0, MidpointRounding.ToEven) * 10m)"
            },
            {
                v => PureQLMath.Truncate(v.Integer, -18L),
                "(object)(v.Integer - (v.Integer % 1000000000000000000L))"
            },
            {
                v => PureQLMath.Truncate(v.Integer, -1L),
                "(object)(v.Integer - (v.Integer % 10L))"
            },
            { v => PureQLMath.Truncate(v.Integer, long.MaxValue), "(object)v.Integer" },
            {
                v => PureQLMath.Truncate(v.Decimal, 27L),
                "(object)(v.Decimal - (v.Decimal % 0.000000000000000000000000001m))"
            },
            { v => PureQLMath.Truncate(v.Decimal, 28L), "(object)v.Decimal" },
            {
                v => PureQLMath.Truncate(v.Decimal, 0L),
                "(object)(v.Decimal - (v.Decimal % 1m))"
            },
            {
                v => PureQLMath.Truncate(v.Decimal, -28L),
                "(object)(v.Decimal - (v.Decimal % 10000000000000000000000000000m))"
            },
            {
                v => PureQLMath.Truncate(v.Decimal, Bound.Digits * -1L),
                "(object)(v.Decimal - (v.Decimal % 100m))"
            },
            {
                v => PureQLMath.Modulo(v.Integer, long.MinValue),
                "(object)(v.Integer % -9223372036854775808L)"
            },
            {
                v => PureQLMath.Modulo(v.Decimal, -0.0000000000000000000000000001m),
                "(object)(v.Decimal % -0.0000000000000000000000000001m)"
            },
            {
                v =>
                    PureQLMath.IntegerDivide(
                        PureQLMath.Modulo(v.Decimal, 3m),
                        PureQLMath.Truncate(v.Decimal2, 1L)
                    ),
                "(object)(long)(((v.Decimal % 3m) - ((v.Decimal % 3m) % (v.Decimal2 - (v.Decimal2 % 0.1m)))) / (v.Decimal2 - (v.Decimal2 % 0.1m)))"
            },
            {
                v =>
                    PureQLMath.Round(
                        PureQLMath.Truncate(v.Integer, -2L),
                        -1L,
                        MidpointRounding.AwayFromZero
                    ),
                "(object)(long)(Math.Round(((decimal)(v.Integer - (v.Integer % 100L)) * 0.1m), 0, MidpointRounding.AwayFromZero) * 10m)"
            },
            {
                v => PureQLText.Position(PureQLText.Replace(v.Text, v.Other, "x"), "x"),
                "(object)(long)(v.Text.Replace(v.Other, \"x\").IndexOf(\"x\") + 1)"
            },
            {
                v => PureQLConversions.AsString(PureQLText.Length(v.Text)),
                "(long)v.Text.Length.ToString()"
            },
            {
                v => PureQLConversions.AsString(PureQLMath.Modulo(v.Integer, -1L)),
                "(v.Integer % -1L).ToString()"
            },
        };

    [Theory]
    [MemberData(nameof(Rewritten))]
    public void FunctionAtAnEdgeBecomesItsBclShape(
        Expression<Func<Values, object>> call,
        string shape
    )
    {
        Assert.Equal(shape, TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    public static TheoryData<Expression<Func<Values, object>>> Kept =>
        [
            v => PureQLMath.Round(v.Integer, -29L, MidpointRounding.AwayFromZero),
            v => PureQLMath.Round(v.Integer, long.MinValue, MidpointRounding.ToEven),
            v => PureQLMath.Round(v.Decimal, long.MinValue, MidpointRounding.ToEven),
            v => PureQLMath.Round(v.Integer, 3L, MidpointRounding.ToZero),
            v => PureQLMath.Round(v.Decimal, 30L, MidpointRounding.ToPositiveInfinity),
            v => PureQLMath.Round(v.Integer, -1L, MidpointRounding.ToNegativeInfinity),
            v => PureQLMath.Round(v.Integer, v.Other2, MidpointRounding.AwayFromZero),
            v =>
                PureQLMath.Round(
                    v.Integer,
                    Bound.Digits + v.Other2,
                    MidpointRounding.ToEven
                ),
            v => PureQLMath.Truncate(v.Integer, v.Other2),
            v => PureQLMath.Truncate(v.Integer, long.MinValue),
            v => PureQLMath.Truncate(v.Decimal, long.MinValue),
            v => PureQLMath.Truncate(v.Decimal, -Bound.Digits * 15L),
            v => PureQLText.Position(v.Text, v.Other, 1L),
            v => PureQLConversions.AsString(v.Time),
            v => PureQLConversions.AsString(v.Instant),
            v => PureQLConversions.AsUuid(v.Text),
            v => (object?)PureQLConversions.TryAsInteger(v.Text) ?? 0L,
            v => PureQLText.TrimStart(v.Text, Removables),
            v => PureQLText.PadEnd(v.Text, 5L, "0"),
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void FunctionAtAnEdgePostgreSqlComputesOtherwiseIsKept(
        Expression<Func<Values, object>> call
    )
    {
        Assert.Equal(TreeText.Of(call.Body), TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    public static TheoryData<Expression<Func<Values, object>>> NullDigits =>
        [
            v =>
                PureQLMath.Round(
                    v.Integer,
                    Bound.Missing!.Value,
                    MidpointRounding.ToEven
                ),
            v => PureQLMath.Truncate(v.Integer, Bound.Missing!.Value),
            v => PureQLMath.Truncate(v.Decimal, Bound.Missing!.Value),
        ];

    // Digits that are closed but have no value (the interpreter reads a null
    // parameter as @digits.Value under a null check): the rule computes the
    // value to choose its shape, and Closed.Value throws
    // InvalidOperationException out of the rewriter. Expected: the call is
    // kept, as for any digits the rule cannot take.
    [Theory(
        Skip = "Bug: closed digits without a value (a null parameter) make the rewriter throw InvalidOperationException"
    )]
    [MemberData(nameof(NullDigits))]
    public void DigitsWithoutAValueAreKept(Expression<Func<Values, object>> call)
    {
        Assert.Equal(TreeText.Of(call.Body), TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    public sealed record Values(
        string Text,
        string Other,
        long Integer,
        long Other2,
        decimal Decimal,
        decimal Decimal2,
        Guid Uuid,
        DateOnly Date,
        TimeOnly Time,
        DateTime Instant
    );

    private sealed class Holder
    {
        public long Digits { get; init; }

        public long? Missing { get; init; }
    }
}
