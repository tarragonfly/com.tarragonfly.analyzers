using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // LINQ calls written as extension calls, "items.Any(...)" or "items?.Any(...)", for the TF8xxx
    // rules that suggest a cheaper call for a particular receiver.
    internal static class LinqCalls
    {
        // The System.Linq.Enumerable method called as receiver.Name(...) or receiver?.Name(...), or null.
        public static IMethodSymbol Method(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            if (Name(invocation) == null) return null;
            if (!(model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method) || method.ReducedFrom == null) return null;
            return SymbolNames.IsNamed(method.ReducedFrom.ContainingType, "System.Linq.Enumerable") ? method : null;
        }

        // The Enumerable method called with this name, or null.
        public static IMethodSymbol Method(SemanticModel model, InvocationExpressionSyntax invocation, string name, CancellationToken ct)
        {
            if (Name(invocation)?.Identifier.ValueText != name) return null;
            return Method(model, invocation, ct);
        }

        // "Any" in items.Any() and items?.Any(), or null for any other kind of call.
        public static SimpleNameSyntax Name(InvocationExpressionSyntax invocation)
        {
            switch (invocation?.Expression)
            {
                case MemberAccessExpressionSyntax access: return access.Name;
                case MemberBindingExpressionSyntax binding: return binding.Name;
                default: return null;
            }
        }

        // "items" in items.Any() and items?.Any(): for ?., the expression before the nearest ?. the
        // call hangs off.
        public static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access) return access.Expression;
            for (SyntaxNode node = invocation; node.Parent != null; node = node.Parent)
            {
                if (node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == node) return conditional.Expression;
            }
            return null;
        }

        public static Location NameLocation(InvocationExpressionSyntax invocation)
        {
            return Name(invocation).GetLocation();
        }

        public static ITypeSymbol ReceiverType(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            ExpressionSyntax receiver = Receiver(invocation);
            return receiver == null ? null : model.GetTypeInfo(receiver, ct).Type;
        }

        // List<T> or a class derived from it, or an array: the types with Find, Exists and TrueForAll.
        public static bool IsListOrArray(ITypeSymbol type)
        {
            return type is IArrayTypeSymbol || SymbolNames.IsOrDerivesFrom(type, "System.Collections.Generic.List`1");
        }
    }
}