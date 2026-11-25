using System.Linq.Expressions;

namespace Pure.RelationalSchema.Storage.PureQL.Projection.PostgreSQL;

// The value of a closed expression: one that reads no parameter of a lambda,
// such as a constant or the bound value of a query parameter. The markers take
// some of their arguments as values (a frame, an offset, a depth), which the
// interpreter computes from literals and parameters.
internal static class Closed
{
    public static T Value<T>(Expression expression)
    {
        return expression is ConstantExpression { Value: T value }
            ? value
            : Expression
                .Lambda<Func<T>>(Expression.Convert(expression, typeof(T)))
                .Compile(preferInterpretation: true)();
    }

    // Whether an expression reads no parameter, so its value can be computed.
    public static bool Is(Expression expression)
    {
        Parameters parameters = new();
        _ = parameters.Visit(expression);
        return !parameters.Found;
    }

    private sealed class Parameters : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _declared = [];

        public bool Found { get; private set; }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            _declared.UnionWith(node.Parameters);
            return base.VisitLambda(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= !_declared.Contains(node);
            return node;
        }
    }
}
