using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The provider of a RewritingTable: every tree it is given is rewritten
// (Rewriter) and handed to the origin's provider, which runs it or refuses
// it with NotSupportedException, as it would the tree as it was.
internal sealed class RewritingProvider(IQueryProvider origin) : IQueryProvider
{
    private readonly IQueryProvider _origin = origin;

    public IQueryable CreateQuery(Expression expression)
    {
        return _origin.CreateQuery(Rewriter.Rewritten(expression));
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return _origin.CreateQuery<TElement>(Rewriter.Rewritten(expression));
    }

    public object? Execute(Expression expression)
    {
        return _origin.Execute(Rewriter.Rewritten(expression));
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _origin.Execute<TResult>(Rewriter.Rewritten(expression));
    }
}
