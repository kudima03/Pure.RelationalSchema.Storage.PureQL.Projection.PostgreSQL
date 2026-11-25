using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// Rewrites a projection's tree for the origin's provider, bottom up: every
// RewritingTable becomes the origin's expression, and every call of the
// interpreter's operators and functions that PostgreSQL computes exactly as
// the PureQL specification defines becomes the translator's marker or a BCL
// shape the translator renders as that SQL (Windows, Recursion, Functions).
// A call rewritten to nothing exact is left as it is: the origin's provider
// refuses it, and the projection runs the tree in memory.
internal sealed class Rewriter : ExpressionVisitor
{
    private Rewriter() { }

    public static Expression Rewritten(Expression tree)
    {
        return new Rewriter().Visit(tree);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        return node.Value is RewritingTable table ? table.Origin.Expression : node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        MethodCallExpression call = (MethodCallExpression)base.VisitMethodCall(node);

        return Windows.Rewritten(call)
            ?? Recursion.Rewritten(call)
            ?? Functions.Rewritten(call)
            ?? call;
    }
}
