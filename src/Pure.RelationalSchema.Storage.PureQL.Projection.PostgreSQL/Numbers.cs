using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The arithmetic PostgreSQL computes exactly as the specification: numeric
// % is the exact remainder of the quotient truncated toward zero (division by
// zero fails), a multiple of the divisor divides exactly, ROUND rounds a
// numeric half away from zero, and multiplication is exact.
internal static class Numbers
{
    // A unit of 10^28, the largest power of ten a decimal holds.
    private const int Places = 28;

    // modulo(left, right): left % right, with the sign of left; nothing is
    // left over -1 in either.
    public static Expression Modulo(MethodCallExpression call)
    {
        return Expression.Modulo(call.Arguments[0], call.Arguments[1]);
    }

    // integerDivide(left, right): (left - left % right) / right, the exact
    // quotient truncated toward zero, as an integer (outside 64 bits the cast
    // fails, as the specification fails).
    public static Expression IntegerDivide(MethodCallExpression call)
    {
        Expression left = call.Arguments[0];
        Expression right = call.Arguments[1];

        return Expression.Convert(
            Expression.Divide(
                Expression.Subtract(left, Expression.Modulo(left, right)),
                right
            ),
            typeof(long)
        );
    }

    // round(value, digits, mode) with digits a literal or a parameter: ROUND
    // to that many places, after multiplying by a power of ten for a negative
    // number of them, then back; digits the value has no more of leave it.
    public static Expression? Round(MethodCallExpression call)
    {
        if (!Closed.Is(call.Arguments[1]) || !Closed.Is(call.Arguments[2]))
        {
            return null;
        }

        Expression value = call.Arguments[0];
        long digits = Closed.Value<long>(call.Arguments[1]);
        MidpointRounding mode = Closed.Value<MidpointRounding>(call.Arguments[2]);
        bool integer = value.Type == typeof(long);

        if (mode is not (MidpointRounding.AwayFromZero or MidpointRounding.ToEven))
        {
            return null;
        }

        if (digits >= (integer ? 0 : Places))
        {
            return value;
        }

        if (digits < -Places)
        {
            return null;
        }

        Expression number = integer ? Expression.Convert(value, typeof(decimal)) : value;
        Expression rounded =
            digits >= 0
                ? Rounded(number, (int)digits, mode)
                : Expression.Multiply(
                    Rounded(Expression.Multiply(number, Power(digits)), 0, mode),
                    Power(-digits)
                );

        return integer ? Expression.Convert(rounded, typeof(long)) : rounded;
    }

    // truncate(value, digits) with digits a literal or a parameter: value
    // less its remainder by the power of ten of the last place kept, toward
    // zero.
    public static Expression? Truncate(MethodCallExpression call)
    {
        if (!Closed.Is(call.Arguments[1]))
        {
            return null;
        }

        Expression value = call.Arguments[0];
        long digits = Closed.Value<long>(call.Arguments[1]);
        bool integer = value.Type == typeof(long);

        if (digits >= (integer ? 0 : Places))
        {
            return value;
        }

        if (digits < -(integer ? 18 : Places))
        {
            return null;
        }

        Expression unit = integer
            ? Expression.Constant((long)Math.Pow(10, -digits))
            : Power(-digits);

        return Expression.Subtract(value, Expression.Modulo(value, unit));
    }

    private static Expression Rounded(
        Expression number,
        int digits,
        MidpointRounding mode
    )
    {
        return Expression.Call(
            typeof(Math).GetMethod(
                nameof(Math.Round),
                [typeof(decimal), typeof(int), typeof(MidpointRounding)]
            )!,
            number,
            Expression.Constant(digits),
            Expression.Constant(mode)
        );
    }

    // 10^exponent as a decimal, exactly: 10^28 down to 10^-28.
    private static ConstantExpression Power(long exponent)
    {
        decimal power = 1m;

        for (long step = 0; step < Math.Abs(exponent); step++)
        {
            power = exponent > 0 ? power * 10m : power / 10m;
        }

        return Expression.Constant(power);
    }
}
