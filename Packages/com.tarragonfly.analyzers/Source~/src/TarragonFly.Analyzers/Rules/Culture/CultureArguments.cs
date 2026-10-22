using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // Whether a call already says which culture to use, shared by the culture rules.
    public static class CultureArguments
    {
        // True when the call passes an IFormatProvider that isn't a plain null. A null provider
        // falls back to the player's culture, the same as leaving it out.
        public static bool PassesFormatProvider(IInvocationOperation invocation)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (!SymbolNames.IsNamed(argument.Parameter.Type, "System.IFormatProvider") || argument.ArgumentKind == ArgumentKind.DefaultValue) continue;
                if (!(argument.Value.Syntax is ExpressionSyntax value) || !IsNull(value)) return true;
            }
            return false;
        }

        // A StringComparison, CultureInfo or CompareOptions parameter.
        public static bool TakesComparison(IMethodSymbol method)
        {
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (SymbolNames.IsNamed(parameter.Type, "System.StringComparison") || SymbolNames.IsNamed(parameter.Type, "System.Globalization.CultureInfo")
                    || SymbolNames.IsNamed(parameter.Type, "System.Globalization.CompareOptions")) return true;
            }
            return false;
        }

        private static bool IsNull(ExpressionSyntax value)
        {
            while (value is ParenthesizedExpressionSyntax parenthesized) value = parenthesized.Expression;
            return value.IsKind(SyntaxKind.NullLiteralExpression);
        }
    }
}