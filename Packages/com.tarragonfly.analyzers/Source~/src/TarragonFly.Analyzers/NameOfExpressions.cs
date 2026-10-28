using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers
{
    // Names inside nameof(...) are only text: nothing is read, written or called there.
    internal static class NameOfExpressions
    {
        // By syntax alone, so a method someone named nameof counts too. Rare enough not to matter.
        public static bool IsNameOf(SyntaxNode node)
        {
            return node is InvocationExpressionSyntax invocation && invocation.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "nameof";
        }

        public static bool IsInsideNameOf(SyntaxNode node)
        {
            for (SyntaxNode parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (IsNameOf(parent)) return true;
            }
            return false;
        }

        public static bool IsInsideNameOf(IOperation operation)
        {
            for (IOperation parent = operation.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is INameOfOperation) return true;
            }
            return false;
        }
    }
}
