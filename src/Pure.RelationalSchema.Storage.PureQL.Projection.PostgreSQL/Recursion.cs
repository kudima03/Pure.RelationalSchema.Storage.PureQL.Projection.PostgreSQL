using System.Linq.Expressions;
using System.Reflection;
using Linq.Expressions.Translation.PostgreSql;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// A recursive subquery, PureQLRecursion.Recursive(seed, level => next,
// maxDepth), is PostgreSQL's WITH RECURSIVE with the same depth limit,
// PostgreSqlRecursion.WithRecursive(seed, level => next, maxDepth): level 0 is
// the seed, each level the rows of next over the level before, until a level
// is empty; every row of every level, duplicates included; and a row deeper
// than maxDepth fails the statement, as it fails the query in the
// specification, instead of being cut off. The translator checks PostgreSQL's
// rules for the recursive term and refuses a next that breaks them (one that
// groups, which a grouped next of the specification does, or reads the level
// in a subquery or on the optional side of an outer join): that recursion
// runs in memory.
internal static class Recursion
{
    private static readonly MethodInfo WithRecursive = typeof(PostgreSqlRecursion)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(PostgreSqlRecursion.WithRecursive)
            && method.GetParameters().Length == 3
        );

    public static Expression? Rewritten(MethodCallExpression call)
    {
        return
            call.Method.DeclaringType != typeof(PureQLRecursion)
            || call.Method.Name != nameof(PureQLRecursion.Recursive)
            || !IsQuery(call.Arguments[0].Type)
            ? null
            : (Expression)
                Expression.Call(
                    WithRecursive.MakeGenericMethod(call.Method.GetGenericArguments()),
                    call.Arguments[0],
                    call.Arguments[1],
                    Expression.Constant(Closed.Value<long>(call.Arguments[2]))
                );
    }

    public static bool IsQuery(Type type)
    {
        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IQueryable<>);
    }
}
