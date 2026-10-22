using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // Shared by the culture rules: which method a call names, and whether it already says which
    // culture to use.
    public static class CultureCalls
    {
        // The method name in x.Name(), x?.Name() and Name(). The bare form covers calls after
        // "using static" and calls from inside the type. Null for something like GetHandler()().
        public static SimpleNameSyntax CalledName(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax access: return access.Name;
                case MemberBindingExpressionSyntax binding: return binding.Name;
                default: return invocation.Expression as SimpleNameSyntax;
            }
        }

        // True when the call passes a non-null IFormatProvider. Null, default and a cast null all fall
        // back to the player's culture, same as leaving the provider out.
        public static bool PassesFormatProvider(IInvocationOperation invocation)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (!SymbolNames.IsNamed(argument.Parameter.Type, "System.IFormatProvider") || argument.ArgumentKind == ArgumentKind.DefaultValue) continue;
                if (!IsNull(argument.Value)) return true;
            }
            return false;
        }

        // True when the call passes a null CultureInfo, which StartsWith and EndsWith treat as the
        // player's culture.
        public static bool PassesNullCulture(IInvocationOperation invocation)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (SymbolNames.IsNamed(argument.Parameter.Type, "System.Globalization.CultureInfo") && IsNull(argument.Value)) return true;
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

        // The compiler folds null, default and (IFormatProvider)null into the same null constant.
        private static bool IsNull(IOperation value) => value.ConstantValue.HasValue && value.ConstantValue.Value == null;
    }
}