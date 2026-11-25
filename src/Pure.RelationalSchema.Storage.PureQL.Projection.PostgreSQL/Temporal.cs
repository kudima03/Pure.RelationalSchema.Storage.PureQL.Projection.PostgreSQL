using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The parts and periods of dates and times without a zone, built from the
// parts PostgreSQL extracts (EXTRACT, as integers) and the constructors the
// translator writes as MAKE_DATE, MAKE_TIME and MAKE_TIMESTAMP: a datetime is
// read and built in UTC, as a timestamp holds it.
internal static class Temporal
{
    // quarter: (month - 1) / 3 + 1, in integers.
    public static Expression Quarter(MethodCallExpression call)
    {
        return Expression.Convert(
            Expression.Add(
                Expression.Divide(
                    Expression.Subtract(Part(call.Arguments[0], "Month"), One),
                    Expression.Constant(3)
                ),
                One
            ),
            typeof(long)
        );
    }

    // dayOfWeek: Monday 1 to Sunday 7, from the day of the week counted from
    // Sunday, 0.
    public static Expression DayOfWeek(MethodCallExpression call)
    {
        return Expression.Convert(
            Expression.Add(FromMonday(call.Arguments[0]), One),
            typeof(long)
        );
    }

    // microsecond: the fraction of the second in microseconds, its
    // milliseconds and the microseconds of the millisecond.
    public static Expression Microsecond(MethodCallExpression call)
    {
        return Expression.Convert(
            Expression.Add(
                Expression.Multiply(
                    Part(call.Arguments[0], "Millisecond"),
                    Expression.Constant(1000)
                ),
                Part(call.Arguments[0], "Microsecond")
            ),
            typeof(long)
        );
    }

    public static Expression StartOfYear(MethodCallExpression call)
    {
        Expression operand = call.Arguments[0];
        return Day(operand, Part(operand, "Year"), One, One);
    }

    // The first day of January, April, July or October: month - (month - 1)
    // % 3.
    public static Expression StartOfQuarter(MethodCallExpression call)
    {
        Expression operand = call.Arguments[0];
        Expression month = Part(operand, "Month");

        return Day(
            operand,
            Part(operand, "Year"),
            Expression.Subtract(
                month,
                Expression.Modulo(Expression.Subtract(month, One), Expression.Constant(3))
            ),
            One
        );
    }

    public static Expression StartOfMonth(MethodCallExpression call)
    {
        Expression operand = call.Arguments[0];
        return Day(operand, Part(operand, "Year"), Part(operand, "Month"), One);
    }

    // The Monday on or before the day: the day less the days since Monday.
    public static Expression StartOfWeek(MethodCallExpression call)
    {
        Expression operand = call.Arguments[0];
        Expression day =
            operand.Type == typeof(DateTime)
                ? Expression.Property(operand, nameof(DateTime.Date))
                : operand;

        return Expression.Call(
            day,
            operand.Type.GetMethod(
                nameof(DateOnly.AddDays),
                [operand.Type == typeof(DateTime) ? typeof(double) : typeof(int)]
            )!,
            operand.Type == typeof(DateTime)
                ? Expression.Convert(
                    Expression.Negate(FromMonday(operand)),
                    typeof(double)
                )
                : Expression.Negate(FromMonday(operand))
        );
    }

    // Midnight of the day.
    public static Expression StartOfDay(MethodCallExpression call)
    {
        return Expression.Property(call.Arguments[0], nameof(DateTime.Date));
    }

    public static Expression StartOfHour(MethodCallExpression call)
    {
        return Clock(call.Arguments[0], "Hour");
    }

    public static Expression StartOfMinute(MethodCallExpression call)
    {
        return Clock(call.Arguments[0], "Hour", "Minute");
    }

    public static Expression StartOfSecond(MethodCallExpression call)
    {
        return Clock(call.Arguments[0], "Hour", "Minute", "Second");
    }

    private static ConstantExpression One { get; } = Expression.Constant(1);

    private static Expression Part(Expression operand, string name)
    {
        return Expression.Property(operand, name);
    }

    // The days since Monday: (day of the week + 6) % 7.
    private static Expression FromMonday(Expression operand)
    {
        return Expression.Modulo(
            Expression.Add(
                Expression.Convert(Part(operand, "DayOfWeek"), typeof(int)),
                Expression.Constant(6)
            ),
            Expression.Constant(7)
        );
    }

    // A day of the operand's type, a date or midnight of a datetime.
    private static Expression Day(
        Expression operand,
        Expression year,
        Expression month,
        Expression day
    )
    {
        return Expression.New(
            operand.Type.GetConstructor([typeof(int), typeof(int), typeof(int)])!,
            year,
            month,
            day
        );
    }

    // The time or the datetime with its clock parts after the named ones zero:
    // new TimeOnly(hour, minute, second) or new DateTime(year, month, day,
    // hour, minute, second).
    private static Expression Clock(Expression operand, params string[] parts)
    {
        string[] clock = ["Hour", "Minute", "Second"];
        Expression[] kept =
        [
            .. clock.Select(part =>
                parts.Contains(part) ? Part(operand, part) : Expression.Constant(0)
            ),
        ];

        return operand.Type == typeof(TimeOnly)
            ? Expression.New(
                typeof(TimeOnly).GetConstructor([typeof(int), typeof(int), typeof(int)])!,
                kept
            )
            : Expression.New(
                typeof(DateTime).GetConstructor([
                    typeof(int),
                    typeof(int),
                    typeof(int),
                    typeof(int),
                    typeof(int),
                    typeof(int),
                ])!,
                [
                    Part(operand, "Year"),
                    Part(operand, "Month"),
                    Part(operand, "Day"),
                    .. kept,
                ]
            );
    }
}
