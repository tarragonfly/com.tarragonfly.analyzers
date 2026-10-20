using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3500: a text operation that depends on the player's language settings, as in "id.ToLower() ==
    // "boss"" or "speed.ToString()" written to a save file.
    //
    // On a Turkish PC "BOSS".ToLower() keeps a dotless i, and on a German one 1.5.ToString() gives
    // "1,5", so IDs stop matching and saves fail to load. Pass CultureInfo.InvariantCulture, use
    // ToLowerInvariant, or StringComparison.Ordinal for comparisons.
    //
    // Checked: string ToUpper() and ToLower(); string IndexOf, LastIndexOf and CompareTo given a
    // string, and string.Compare, without a StringComparison or culture; ToString() without arguments
    // on float, double, decimal and DateTime. int.ToString, StartsWith and formats without a culture
    // aren't checked. Reported on the method name. Game code only.
    //
    // Calls through "?." count too, as in "name?.ToLower()".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CultureSensitiveTextAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3500",
            "Text operation that depends on the player's culture",
            "'{0}' uses the player's culture, so results change with their language settings; pass CultureInfo.InvariantCulture or StringComparison.Ordinal",
            "Culture",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name
                    : invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : null;
                if (name == null) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (DependsOnCulture(method)) context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), method.Name));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool DependsOnCulture(IMethodSymbol method)
        {
            switch (method.ContainingType.SpecialType)
            {
                case SpecialType.System_String:
                    switch (method.Name)
                    {
                        case "ToUpper":
                        case "ToLower":
                            return method.Parameters.Length == 0;
                        case "IndexOf":
                        case "LastIndexOf":
                        case "CompareTo":
                            return method.Parameters.Length > 0 && method.Parameters[0].Type.SpecialType == SpecialType.System_String && !TakesComparison(method);
                        case "Compare":
                            return method.IsStatic && !TakesComparison(method);
                        default:
                            return false;
                    }
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_DateTime:
                    return method.Name == "ToString" && method.Parameters.Length == 0;
                default:
                    return false;
            }
        }

        // A StringComparison, CultureInfo or CompareOptions parameter.
        private static bool TakesComparison(IMethodSymbol method)
        {
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (SymbolNames.IsNamed(parameter.Type, "System.StringComparison") || SymbolNames.IsNamed(parameter.Type, "System.Globalization.CultureInfo")
                    || SymbolNames.IsNamed(parameter.Type, "System.Globalization.CompareOptions")) return true;
            }
            return false;
        }
    }
}