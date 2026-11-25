using System.Linq.Expressions;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// Each function of the interpreter that PostgreSQL computes exactly as the
// specification, in and out, and the functions and the arguments left as they
// are.
public sealed class FunctionTests
{
    private static readonly string[] Removables = [" "];

    private static readonly Holder Bound = new()
    {
        Digits = 2,
        Mode = MidpointRounding.ToEven,
    };

    public static TheoryData<Expression<Func<Values, object>>, string> Rewritten =>
        new()
        {
            { v => PureQLText.Length(v.Text), "(object)(long)v.Text.Length" },
            {
                v => PureQLText.Position(v.Text, v.Other),
                "(object)(long)(v.Text.IndexOf(v.Other) + 1)"
            },
            {
                v => PureQLText.Replace(v.Text, v.Other, "-"),
                "v.Text.Replace(v.Other, \"-\")"
            },
            { v => PureQLConversions.AsString(v.Integer), "v.Integer.ToString()" },
            { v => PureQLConversions.AsString(v.Uuid), "v.Uuid.ToString()" },
            {
                v => PureQLMath.Modulo(v.Integer, v.Other2),
                "(object)(v.Integer % v.Other2)"
            },
            { v => PureQLMath.Modulo(v.Decimal, 0.05m), "(object)(v.Decimal % 0.05m)" },
            {
                v => PureQLMath.IntegerDivide(v.Decimal, v.Decimal2),
                "(object)(long)((v.Decimal - (v.Decimal % v.Decimal2)) / v.Decimal2)"
            },
            {
                v => PureQLMath.Round(v.Decimal, 2L, MidpointRounding.AwayFromZero),
                "(object)Math.Round(v.Decimal, 2, MidpointRounding.AwayFromZero)"
            },
            {
                v => PureQLMath.Round(v.Decimal, Bound.Digits, Bound.Mode),
                "(object)Math.Round(v.Decimal, 2, MidpointRounding.ToEven)"
            },
            {
                v => PureQLMath.Round(v.Decimal, -2L, MidpointRounding.AwayFromZero),
                "(object)(Math.Round((v.Decimal * 0.01m), 0, MidpointRounding.AwayFromZero) * 100m)"
            },
            {
                v => PureQLMath.Round(v.Decimal, 28L, MidpointRounding.AwayFromZero),
                "(object)v.Decimal"
            },
            {
                v => PureQLMath.Round(v.Integer, -1L, MidpointRounding.ToEven),
                "(object)(long)(Math.Round(((decimal)v.Integer * 0.1m), 0, MidpointRounding.ToEven) * 10m)"
            },
            {
                v => PureQLMath.Round(v.Integer, 3L, MidpointRounding.AwayFromZero),
                "(object)v.Integer"
            },
            {
                v => PureQLMath.Truncate(v.Decimal, 2L),
                "(object)(v.Decimal - (v.Decimal % 0.01m))"
            },
            {
                v => PureQLMath.Truncate(v.Decimal, -2L),
                "(object)(v.Decimal - (v.Decimal % 100m))"
            },
            { v => PureQLMath.Truncate(v.Decimal, 30L), "(object)v.Decimal" },
            {
                v => PureQLMath.Truncate(v.Integer, -3L),
                "(object)(v.Integer - (v.Integer % 1000L))"
            },
            { v => PureQLMath.Truncate(v.Integer, 0L), "(object)v.Integer" },
            {
                v => PureQLTime.Quarter(v.Date),
                "(object)(long)(((v.Date.Month - 1) / 3) + 1)"
            },
            {
                v => PureQLTime.DayOfWeek(v.Date),
                "(object)(long)((((int)v.Date.DayOfWeek + 6) % 7) + 1)"
            },
            {
                v => PureQLTime.Microsecond(v.Time),
                "(object)(long)((v.Time.Millisecond * 1000) + v.Time.Microsecond)"
            },
            {
                v => PureQLTime.StartOfYear(v.Date),
                "(object)new DateOnly(v.Date.Year, 1, 1)"
            },
            {
                v => PureQLTime.StartOfQuarter(v.Date),
                "(object)new DateOnly(v.Date.Year, (v.Date.Month - ((v.Date.Month - 1) % 3)), 1)"
            },
            {
                v => PureQLTime.StartOfMonth(v.Date),
                "(object)new DateOnly(v.Date.Year, v.Date.Month, 1)"
            },
            {
                v => PureQLTime.StartOfWeek(v.Date),
                "(object)v.Date.AddDays(-(((int)v.Date.DayOfWeek + 6) % 7))"
            },
            {
                v => PureQLTime.StartOfHour(v.Time),
                "(object)new TimeOnly(v.Time.Hour, 0, 0)"
            },
            {
                v => PureQLTime.StartOfMinute(v.Time),
                "(object)new TimeOnly(v.Time.Hour, v.Time.Minute, 0)"
            },
            {
                v => PureQLTime.StartOfSecond(v.Time),
                "(object)new TimeOnly(v.Time.Hour, v.Time.Minute, v.Time.Second)"
            },
            {
                v => PureQLTime.StartOfYear(v.Instant),
                "(object)new DateTime(v.Instant.Year, 1, 1)"
            },
            {
                v => PureQLTime.StartOfQuarter(v.Instant),
                "(object)new DateTime(v.Instant.Year, (v.Instant.Month - ((v.Instant.Month - 1) % 3)), 1)"
            },
            {
                v => PureQLTime.StartOfMonth(v.Instant),
                "(object)new DateTime(v.Instant.Year, v.Instant.Month, 1)"
            },
            {
                v => PureQLTime.StartOfWeek(v.Instant),
                "(object)v.Instant.Date.AddDays((double)-(((int)v.Instant.DayOfWeek + 6) % 7))"
            },
            { v => PureQLTime.StartOfDay(v.Instant), "(object)v.Instant.Date" },
            {
                v => PureQLTime.StartOfHour(v.Instant),
                "(object)new DateTime(v.Instant.Year, v.Instant.Month, v.Instant.Day, v.Instant.Hour, 0, 0)"
            },
            {
                v => PureQLTime.StartOfMinute(v.Instant),
                "(object)new DateTime(v.Instant.Year, v.Instant.Month, v.Instant.Day, v.Instant.Hour, v.Instant.Minute, 0)"
            },
            {
                v => PureQLTime.StartOfSecond(v.Instant),
                "(object)new DateTime(v.Instant.Year, v.Instant.Month, v.Instant.Day, v.Instant.Hour, v.Instant.Minute, v.Instant.Second)"
            },
        };

    [Theory]
    [MemberData(nameof(Rewritten))]
    public void FunctionBecomesItsBclShape(
        Expression<Func<Values, object>> call,
        string shape
    )
    {
        Assert.Equal(shape, TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    public static TheoryData<Expression<Func<Values, object>>> Kept =>
        [
            v => PureQLText.Upper(v.Text),
            v => PureQLText.Lower(v.Text),
            v => PureQLText.FoldCase(v.Text),
            v => PureQLText.RemoveDiacritics(v.Text),
            v => PureQLText.Trim(v.Text, Removables),
            v => PureQLText.Substring(v.Text, 2L, 3L),
            v => PureQLText.Position(v.Text, v.Other, 2L),
            v => PureQLText.SplitPart(v.Text, ",", 2L),
            v => PureQLText.PadStart(v.Text, 5L, "0"),
            v => PureQLText.Repeat(v.Text, 2L),
            v => PureQLText.Reverse(v.Text),
            v => PureQLText.ConcatWith(",", new[] { v.Text, v.Other }),
            v => PureQLText.MatchesRegex(v.Text, "a+"),
            v => PureQLConversions.AsString(v.Decimal),
            v => PureQLConversions.AsString(v.Date),
            v => PureQLConversions.AsString(true),
            v => PureQLConversions.AsInteger(v.Text),
            v => (object?)PureQLConversions.TryAsDecimal(v.Text) ?? 0m,
            v => PureQLMath.Power(v.Decimal, 2m),
            v => PureQLMath.Sqrt(v.Decimal),
            v => PureQLMath.Sin(v.Decimal),
            v => PureQLMath.Round(v.Decimal, v.Integer, MidpointRounding.AwayFromZero),
            v => PureQLMath.Round(v.Decimal, -29L, MidpointRounding.AwayFromZero),
            v => PureQLMath.Round(v.Decimal, 2L, MidpointRounding.ToZero),
            v => PureQLMath.Truncate(v.Decimal, v.Integer),
            v => PureQLMath.Truncate(v.Decimal, -29L),
            v => PureQLMath.Truncate(v.Integer, -19L),
            v => PureQLTime.Week(v.Date),
            v => PureQLTime.WeekYear(v.Date),
            v => PureQLTime.StartOfMonth(v.Instant, "Europe/Berlin"),
            v => PureQLTime.LocalDate(v.Instant, "Europe/Berlin"),
            v => PureQLTime.DiffMonths(v.Date, v.Date),
            v => PureQLTime.TimeFromParts(1L, 2L, 3.5m),
            v => (object?)PureQLAggregates.Median(new decimal?[] { v.Decimal }) ?? 0m,
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void FunctionPostgreSqlComputesOtherwiseIsKept(
        Expression<Func<Values, object>> call
    )
    {
        Assert.Equal(TreeText.Of(call.Body), TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    [Fact]
    public void FunctionIsRewrittenInsideTheOperandsOfAnother()
    {
        Expression<Func<Values, long>> call = v =>
            PureQLText.Length(PureQLText.Replace(v.Text, "a", "bb"));

        Assert.Equal(
            "(long)v.Text.Replace(\"a\", \"bb\").Length",
            TreeText.Of(Rewriter.Rewritten(call.Body))
        );
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

        public MidpointRounding Mode { get; init; }
    }
}
