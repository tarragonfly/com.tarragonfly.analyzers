using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // LINQ calls written as extension calls, "items.Any(...)", for the TF8xxx rules that suggest a
    // cheaper call for a particular receiver.
    internal static class LinqCalls
    {
        // The System.Linq.Enumerable method called as receiver.Name(...), or null.
        public static IMethodSymbol Method(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax)) return null;
            if (!(model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method) || method.ReducedFrom == null) return null;
            return SymbolNames.IsNamed(method.ReducedFrom.ContainingType, "System.Linq.Enumerable") ? method : null;
        }

        // The Enumerable method called with this name, or null.
        public static IMethodSymbol Method(SemanticModel model, InvocationExpressionSyntax invocation, string name, CancellationToken ct)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != name) return null;
            return Method(model, invocation, ct);
        }

        public static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation)
        {
            return ((MemberAccessExpressionSyntax)invocation.Expression).Expression;
        }

        public static Location NameLocation(InvocationExpressionSyntax invocation)
        {
            return ((MemberAccessExpressionSyntax)invocation.Expression).Name.GetLocation();
        }

        public static ITypeSymbol ReceiverType(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            return model.GetTypeInfo(Receiver(invocation), ct).Type;
        }

        // List<T> or an array, the two types with Find, Exists and TrueForAll.
        public static bool IsListOrArray(ITypeSymbol type)
        {
            return type is IArrayTypeSymbol || SymbolNames.IsNamed(type?.OriginalDefinition, "System.Collections.Generic.List`1");
        }
    }
}