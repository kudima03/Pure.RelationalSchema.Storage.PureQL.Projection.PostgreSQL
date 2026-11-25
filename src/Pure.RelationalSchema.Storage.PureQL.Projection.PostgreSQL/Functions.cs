using System.Linq.Expressions;
using System.Reflection;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The interpreter's functions that a BCL shape computes in PostgreSQL exactly
// as the specification defines them, rewritten into that shape; the
// translator renders it. A rewritten tree only ever runs in PostgreSQL (in
// memory the projection runs its own tree), so the shape is chosen for the SQL
// it becomes, not for what .NET would compute with it. The interpreter's
// functions take non-null operands and the tree lifts them, so the shapes do
// not either.
//
// Rewritten (Text, Numbers, Dates and times):
// - length: LENGTH, the number of code points; position without start:
//   STRPOS, from 1, 0 when absent and 1 for an empty pattern; replace:
//   REPLACE, which leaves the operand as it is for an empty pattern.
// - asString of an integer and of a uuid: their text in PostgreSQL, which is
//   the literal's spelling.
// - modulo and integerDivide: PostgreSQL's numeric % is the exact remainder
//   of the quotient truncated toward zero, and an exact multiple of the
//   divisor divides exactly; bigint % -1 is 0.
// - round and truncate with digits that are a literal or a parameter: ROUND
//   to that many places (half away from zero, or half to even, which the
//   translator decides itself), scaled by an exact multiplication by a power
//   of ten for a negative number of places, and truncation as the operand
//   less its exact remainder by that power.
// - quarter, dayOfWeek and microsecond, and startOfYear ... startOfSecond
//   without a zone, built from the parts PostgreSQL extracts.
//
// Left as they are, so they run in memory:
// - upper, lower, foldCase, removeDiacritics: PostgreSQL maps case by the
//   collation of the database, not by Unicode's full mappings, and has no
//   case folding or diacritics removal; the regular expressions: PostgreSQL's
//   dialect is another; trim, substring, position with a start, splitPart,
//   padStart, padEnd, repeat, reverse and concatWith: their PostgreSQL
//   functions differ at the edges (sets of characters, negative positions,
//   truncation, errors) or have no BCL shape the translator renders.
// - asString of a decimal, a boolean, a date, a time or a datetime, which
//   PostgreSQL writes otherwise (trailing zeros, its date style), and every
//   reading of text (as..., tryAs...), whose casts accept spellings the
//   specification rejects.
// - powers, roots, logarithms and trigonometry, correctly rounded to 28
//   digits in the specification and computed by PostgreSQL to fewer.
// - round and truncate with computed digits, and the parts and periods of a
//   zone (the first instant of a local time, the zone database), the ISO
//   week, the months between and the month arithmetic of a zone, and dates
//   and times from parts, whose seconds PostgreSQL takes as a double.
internal static class Functions
{
    private static readonly Dictionary<
        string,
        Func<MethodCallExpression, Expression?>
    > All = new()
    {
        [Key(typeof(PureQLText), nameof(PureQLText.Length), typeof(string))] =
            Text.Length,
        [
            Key(
                typeof(PureQLText),
                nameof(PureQLText.Position),
                typeof(string),
                typeof(string)
            )
        ] = Text.Position,
        [
            Key(
                typeof(PureQLText),
                nameof(PureQLText.Replace),
                typeof(string),
                typeof(string),
                typeof(string)
            )
        ] = Text.Replace,
        [
            Key(
                typeof(PureQLConversions),
                nameof(PureQLConversions.AsString),
                typeof(long)
            )
        ] = Text.Written,
        [
            Key(
                typeof(PureQLConversions),
                nameof(PureQLConversions.AsString),
                typeof(Guid)
            )
        ] = Text.Written,
        [Key(typeof(PureQLMath), nameof(PureQLMath.Modulo), typeof(long), typeof(long))] =
            Numbers.Modulo,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.Modulo),
                typeof(decimal),
                typeof(decimal)
            )
        ] = Numbers.Modulo,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.IntegerDivide),
                typeof(decimal),
                typeof(decimal)
            )
        ] = Numbers.IntegerDivide,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.Round),
                typeof(decimal),
                typeof(long),
                typeof(MidpointRounding)
            )
        ] = Numbers.Round,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.Round),
                typeof(long),
                typeof(long),
                typeof(MidpointRounding)
            )
        ] = Numbers.Round,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.Truncate),
                typeof(decimal),
                typeof(long)
            )
        ] = Numbers.Truncate,
        [
            Key(
                typeof(PureQLMath),
                nameof(PureQLMath.Truncate),
                typeof(long),
                typeof(long)
            )
        ] = Numbers.Truncate,
        [Key(typeof(PureQLTime), nameof(PureQLTime.Quarter), typeof(DateOnly))] =
            Temporal.Quarter,
        [Key(typeof(PureQLTime), nameof(PureQLTime.DayOfWeek), typeof(DateOnly))] =
            Temporal.DayOfWeek,
        [Key(typeof(PureQLTime), nameof(PureQLTime.Microsecond), typeof(TimeOnly))] =
            Temporal.Microsecond,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfYear), typeof(DateOnly))] =
            Temporal.StartOfYear,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfQuarter), typeof(DateOnly))] =
            Temporal.StartOfQuarter,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfMonth), typeof(DateOnly))] =
            Temporal.StartOfMonth,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfWeek), typeof(DateOnly))] =
            Temporal.StartOfWeek,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfHour), typeof(TimeOnly))] =
            Temporal.StartOfHour,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfMinute), typeof(TimeOnly))] =
            Temporal.StartOfMinute,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfSecond), typeof(TimeOnly))] =
            Temporal.StartOfSecond,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfYear), typeof(DateTime))] =
            Temporal.StartOfYear,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfQuarter), typeof(DateTime))] =
            Temporal.StartOfQuarter,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfMonth), typeof(DateTime))] =
            Temporal.StartOfMonth,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfWeek), typeof(DateTime))] =
            Temporal.StartOfWeek,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfDay), typeof(DateTime))] =
            Temporal.StartOfDay,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfHour), typeof(DateTime))] =
            Temporal.StartOfHour,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfMinute), typeof(DateTime))] =
            Temporal.StartOfMinute,
        [Key(typeof(PureQLTime), nameof(PureQLTime.StartOfSecond), typeof(DateTime))] =
            Temporal.StartOfSecond,
    };

    public static Expression? Rewritten(MethodCallExpression call)
    {
        return All.TryGetValue(
            Key(call.Method),
            out Func<MethodCallExpression, Expression?>? rule
        )
            ? rule(call)
            : null;
    }

    // A method by its type, name and parameter types.
    private static string Key(Type owner, string name, params Type[] parameters)
    {
        string types = string.Join(",", parameters.Select(type => type.FullName));
        return $"{owner.FullName}.{name}({types})";
    }

    private static string Key(MethodInfo method)
    {
        return Key(
            method.DeclaringType!,
            method.Name,
            [.. method.GetParameters().Select(parameter => parameter.ParameterType)]
        );
    }
}
