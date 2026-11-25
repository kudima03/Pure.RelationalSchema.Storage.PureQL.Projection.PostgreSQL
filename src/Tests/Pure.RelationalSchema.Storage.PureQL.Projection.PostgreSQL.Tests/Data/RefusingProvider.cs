using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL.Tests.Data;

// The provider of RefusingTables: it records every tree it is given, as the
// rewriting provider hands it over, and refuses it with
// NotSupportedException, so a projection runs its own tree in memory.
internal sealed class RefusingProvider : IQueryProvider
{
    public List<Expression> Queries { get; } = [];

    public IQueryable CreateQuery(Expression expression)
    {
        throw Refused(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        throw Refused(expression);
    }

    public object? Execute(Expression expression)
    {
        throw Refused(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        throw Refused(expression);
    }

    private NotSupportedException Refused(Expression expression)
    {
        Queries.Add(expression);
        return new NotSupportedException("The query was recorded, not run.");
    }
}
