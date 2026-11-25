using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The string functions PostgreSQL computes as the specification: lengths and
// positions in code points, matching by code points (UTF-8 bytes match only
// at the start of a code point).
internal static class Text
{
    // length: LENGTH(operand).
    public static Expression Length(MethodCallExpression call)
    {
        return Expression.Convert(
            Expression.Property(call.Arguments[0], nameof(string.Length)),
            typeof(long)
        );
    }

    // position(operand, pattern): STRPOS(operand, pattern), which the
    // translator writes for IndexOf(pattern) + 1.
    public static Expression Position(MethodCallExpression call)
    {
        return Expression.Convert(
            Expression.Add(
                Expression.Call(
                    call.Arguments[0],
                    typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])!,
                    call.Arguments[1]
                ),
                Expression.Constant(1)
            ),
            typeof(long)
        );
    }

    // replace(operand, pattern, replacement): REPLACE, every occurrence from
    // the start, none overlapping, none for an empty pattern.
    public static Expression Replace(MethodCallExpression call)
    {
        return Expression.Call(
            call.Arguments[0],
            typeof(string).GetMethod(
                nameof(string.Replace),
                [typeof(string), typeof(string)]
            )!,
            call.Arguments[1],
            call.Arguments[2]
        );
    }

    // asString of an integer or a uuid: CAST(operand AS text), "-12" and
    // "00000001-0000-0000-0000-000000000000" as their literals are written.
    public static Expression Written(MethodCallExpression call)
    {
        return Expression.Call(
            call.Arguments[0],
            call.Arguments[0].Type.GetMethod(nameof(ToString), Type.EmptyTypes)!
        );
    }
}
