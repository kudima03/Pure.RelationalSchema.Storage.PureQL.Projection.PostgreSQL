using System.Linq.Expressions;
using System.Reflection;
using Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Rewriting;

// The edges of the rewritten parts and periods of dates and times, no Docker:
// the shapes of rewritten functions nested in one another and over another
// function's result; the overloads of a zone, the ISO week, the months between
// and the parts constructors left as they are; and the .NET meaning of every
// rewritten shape over the ends of the calendar and the clock, as the
// translator computes it in memory where the operand is a literal or a
// parameter (a subtree without a row is evaluated, not written as SQL).
public sealed class TemporalEdgeTests
{
    public static TheoryData<Expression<Func<Values, object>>, string> Nested =>
        new()
        {
            {
                v => PureQLTime.Quarter(PureQLTime.StartOfQuarter(v.Date)),
                "(object)(long)(((new DateOnly(v.Date.Year, (v.Date.Month - ((v.Date.Month"
                    + " - 1) % 3)), 1).Month - 1) / 3) + 1)"
            },
            {
                v => PureQLTime.DayOfWeek(PureQLTime.StartOfWeek(v.Date)),
                "(object)(long)((((int)v.Date.AddDays(-(((int)v.Date.DayOfWeek + 6) % 7))"
                    + ".DayOfWeek + 6) % 7) + 1)"
            },
            {
                v => PureQLTime.StartOfWeek(PureQLTime.StartOfMonth(v.Date)),
                "(object)new DateOnly(v.Date.Year, v.Date.Month, 1).AddDays(-(((int)new"
                    + " DateOnly(v.Date.Year, v.Date.Month, 1).DayOfWeek + 6) % 7))"
            },
            {
                v => PureQLTime.StartOfYear(PureQLTime.StartOfWeek(v.Date)),
                "(object)new DateOnly(v.Date.AddDays(-(((int)v.Date.DayOfWeek + 6) % 7))"
                    + ".Year, 1, 1)"
            },
            {
                v => PureQLTime.StartOfDay(PureQLTime.StartOfWeek(v.Instant)),
                "(object)v.Instant.Date.AddDays((double)-(((int)v.Instant.DayOfWeek + 6)"
                    + " % 7)).Date"
            },
            {
                v => PureQLTime.StartOfHour(PureQLTime.StartOfDay(v.Instant)),
                "(object)new DateTime(v.Instant.Date.Year, v.Instant.Date.Month,"
                    + " v.Instant.Date.Day, v.Instant.Date.Hour, 0, 0)"
            },
            {
                v => PureQLTime.Microsecond(PureQLTime.StartOfSecond(v.Time)),
                "(object)(long)((new TimeOnly(v.Time.Hour, v.Time.Minute, v.Time.Second)"
                    + ".Millisecond * 1000) + new TimeOnly(v.Time.Hour, v.Time.Minute,"
                    + " v.Time.Second).Microsecond)"
            },
            {
                v => PureQLTime.StartOfMinute(PureQLTime.StartOfHour(v.Time)),
                "(object)new TimeOnly(new TimeOnly(v.Time.Hour, 0, 0).Hour, new"
                    + " TimeOnly(v.Time.Hour, 0, 0).Minute, 0)"
            },
            {
                v => PureQLTime.Quarter(DateOnly.FromDateTime(v.Instant)),
                "(object)(long)(((DateOnly.FromDateTime(v.Instant).Month - 1) / 3) + 1)"
            },
            {
                v => PureQLTime.Microsecond(TimeOnly.FromDateTime(v.Instant)),
                "(object)(long)((TimeOnly.FromDateTime(v.Instant).Millisecond * 1000) +"
                    + " TimeOnly.FromDateTime(v.Instant).Microsecond)"
            },
            {
                v => PureQLTime.StartOfMonth(v.Date.AddDays(v.Days)),
                "(object)new DateOnly(v.Date.AddDays(v.Days).Year,"
                    + " v.Date.AddDays(v.Days).Month, 1)"
            },
        };

    [Theory]
    [MemberData(nameof(Nested))]
    public void FunctionOverAnotherFunctionBecomesItsShapeOverTheOther(
        Expression<Func<Values, object>> call,
        string shape
    )
    {
        Assert.Equal(shape, TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    // A part of a datetime in a zone is the part of its local date or time,
    // which the interpreter reads with PureQLTime.LocalDate or LocalTime: the
    // part is rewritten, the local date is not, so the provider still refuses
    // the tree and the projection runs it in memory.
    [Fact]
    public void PartOfALocalDateKeepsTheLocalDate()
    {
        Expression<Func<Values, long>> call = v =>
            PureQLTime.Quarter(PureQLTime.LocalDate(v.Instant, "Asia/Kolkata"));

        Assert.Equal(
            "(long)(((PureQLTime.LocalDate(v.Instant, \"Asia/Kolkata\").Month - 1) / 3)"
                + " + 1)",
            TreeText.Of(Rewriter.Rewritten(call.Body))
        );
    }

    [Fact]
    public void MicrosecondOfALocalTimeKeepsTheLocalTime()
    {
        Expression<Func<Values, long>> call = v =>
            PureQLTime.Microsecond(PureQLTime.LocalTime(v.Instant, "Europe/Berlin"));

        Assert.Contains(
            "PureQLTime.LocalTime(v.Instant, \"Europe/Berlin\")",
            TreeText.Of(Rewriter.Rewritten(call.Body)),
            StringComparison.Ordinal
        );
    }

    public static TheoryData<Expression<Func<Values, object>>> Kept =>
        [
            v => PureQLTime.StartOfYear(v.Instant, "Europe/Berlin"),
            v => PureQLTime.StartOfQuarter(v.Instant, "Europe/Berlin"),
            v => PureQLTime.StartOfWeek(v.Instant, "America/New_York"),
            v => PureQLTime.StartOfDay(v.Instant, "Asia/Kolkata"),
            v => PureQLTime.StartOfHour(v.Instant, "Asia/Kolkata"),
            v => PureQLTime.StartOfMinute(v.Instant, "Asia/Kolkata"),
            v => PureQLTime.StartOfDay(v.Instant, v.Zone),
            v => PureQLTime.LocalTime(v.Instant, "Europe/Berlin"),
            v => PureQLTime.DatetimeFromParts(v.Date, v.Time, "Europe/Berlin"),
            v => PureQLTime.DiffMonths(v.Instant, v.Instant),
            v => PureQLTime.DiffMonths(v.Instant, v.Instant, "Europe/Berlin"),
            v => PureQLTime.AddMonths(v.Instant, 1L, "Europe/Berlin"),
            v => PureQLTime.TimeFromParts(v.Days, 0L, 0.0000005m),
            v => PureQLTime.Week(DateOnly.FromDateTime(v.Instant)),
            v => PureQLTime.WeekYear(DateOnly.FromDateTime(v.Instant)),
            v => PureQLTime.IsZone(v.Zone),
        ];

    [Theory]
    [MemberData(nameof(Kept))]
    public void TemporalFunctionPostgreSqlComputesOtherwiseIsKept(
        Expression<Func<Values, object>> call
    )
    {
        Assert.Equal(TreeText.Of(call.Body), TreeText.Of(Rewriter.Rewritten(call.Body)));
    }

    // Every rewritten overload of PureQLTime, by name and operand type.
    public static TheoryData<string, Type> Overloads =>
        new()
        {
            { nameof(PureQLTime.Quarter), typeof(DateOnly) },
            { nameof(PureQLTime.DayOfWeek), typeof(DateOnly) },
            { nameof(PureQLTime.StartOfYear), typeof(DateOnly) },
            { nameof(PureQLTime.StartOfQuarter), typeof(DateOnly) },
            { nameof(PureQLTime.StartOfMonth), typeof(DateOnly) },
            { nameof(PureQLTime.StartOfWeek), typeof(DateOnly) },
            { nameof(PureQLTime.Microsecond), typeof(TimeOnly) },
            { nameof(PureQLTime.StartOfHour), typeof(TimeOnly) },
            { nameof(PureQLTime.StartOfMinute), typeof(TimeOnly) },
            { nameof(PureQLTime.StartOfSecond), typeof(TimeOnly) },
            { nameof(PureQLTime.StartOfYear), typeof(DateTime) },
            { nameof(PureQLTime.StartOfQuarter), typeof(DateTime) },
            { nameof(PureQLTime.StartOfMonth), typeof(DateTime) },
            { nameof(PureQLTime.StartOfWeek), typeof(DateTime) },
            { nameof(PureQLTime.StartOfDay), typeof(DateTime) },
            { nameof(PureQLTime.StartOfHour), typeof(DateTime) },
            { nameof(PureQLTime.StartOfMinute), typeof(DateTime) },
            { nameof(PureQLTime.StartOfSecond), typeof(DateTime) },
        };

    // The shape no longer calls PureQLTime, and its .NET meaning is the
    // function's over the first and last days of the calendar, leap days of
    // 1900, 2000 and 2024, the Gregorian reform, every tick of the edges of a
    // second and a day, and thousands of values between; a datetime's instant,
    // whatever its kind (the translator writes a datetime literal as a
    // timestamp whatever its kind).
    [Theory]
    [MemberData(nameof(Overloads))]
    public void ShapeMeansTheFunctionOverTheEndsOfTheCalendarAndTheClock(
        string name,
        Type operand
    )
    {
        MethodInfo method = typeof(PureQLTime).GetMethod(name, [operand])!;
        ParameterExpression value = Expression.Parameter(operand, "value");
        Expression call = Expression.Call(method, value);
        Expression shape = Rewriter.Rewritten(call);
        Func<object, object> function = Compiled(call, value);
        Func<object, object> rewritten = Compiled(shape, value);

        Assert.DoesNotContain("PureQLTime", TreeText.Of(shape), StringComparison.Ordinal);

        foreach (object input in Inputs(operand))
        {
            Assert.Equal(Instant(function(input)), Instant(rewritten(input)));
        }
    }

    private static Func<object, object> Compiled(
        Expression body,
        ParameterExpression value
    )
    {
        ParameterExpression boxed = Expression.Parameter(typeof(object), "boxed");
        Func<object, object> compiled = Expression
            .Lambda<Func<object, object>>(
                Expression.Block(
                    [value],
                    Expression.Assign(value, Expression.Convert(boxed, value.Type)),
                    Expression.Convert(body, typeof(object))
                ),
                boxed
            )
            .Compile();

        return compiled;
    }

    private static object Instant(object value)
    {
        return value is DateTime instant ? instant.Ticks : value;
    }

    private static IEnumerable<object> Inputs(Type operand)
    {
        return operand == typeof(DateOnly) ? Days().Select(day => (object)day)
            : operand == typeof(TimeOnly)
                ? Ticks().Select(ticks => (object)new TimeOnly(ticks))
            : Instants().Select(instant => (object)instant);
    }

    private static IEnumerable<DateOnly> Days()
    {
        (DateOnly From, int Count)[] runs =
        [
            (DateOnly.MinValue, 800),
            (new DateOnly(1582, 9, 25), 30),
            (new DateOnly(1899, 12, 1), 130),
            (new DateOnly(1999, 12, 1), 430),
            (new DateOnly(2023, 12, 20), 80),
            (new DateOnly(9999, 9, 1), 122),
        ];

        foreach ((DateOnly from, int count) in runs)
        {
            for (int day = 0; day < count; day++)
            {
                yield return from.AddDays(day);
            }
        }

        for (int number = 0; number <= DateOnly.MaxValue.DayNumber; number += 997)
        {
            yield return DateOnly.FromDayNumber(number);
        }

        yield return DateOnly.MaxValue;
    }

    private static IEnumerable<long> Ticks()
    {
        long[] edges =
        [
            0,
            1,
            5,
            9,
            10,
            11,
            9_999,
            10_000,
            10_001,
            9_999_999,
            10_000_000,
            599_999_999,
            600_000_000,
            35_999_999_999,
            36_000_000_000,
            450_000_000_005,
            863_999_999_990,
            TimeOnly.MaxValue.Ticks,
        ];
        Random random = new(20241127);

        return
        [
            .. edges,
            .. Enumerable
                .Range(0, 3000)
                .Select(_ => random.NextInt64(TimeOnly.MaxValue.Ticks)),
        ];
    }

    private static IEnumerable<DateTime> Instants()
    {
        DateTimeKind[] kinds = [DateTimeKind.Utc, DateTimeKind.Unspecified];
        long[] clock = [.. Ticks().Take(40)];

        foreach (DateOnly day in Days().Where((_, index) => index % 3 == 0))
        {
            foreach (long ticks in clock.Where((_, index) => index % 5 == day.Day % 5))
            {
                yield return new DateTime(
                    day.ToDateTime(TimeOnly.MinValue).Ticks + ticks,
                    kinds[day.DayNumber % 2]
                );
            }
        }

        yield return DateTime.MinValue;
        yield return DateTime.MaxValue;
        yield return new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Local);
    }

    public sealed record Values(DateOnly Date, TimeOnly Time, DateTime Instant, int Days)
    {
        public string Zone { get; init; } = "Europe/Berlin";
    }
}
