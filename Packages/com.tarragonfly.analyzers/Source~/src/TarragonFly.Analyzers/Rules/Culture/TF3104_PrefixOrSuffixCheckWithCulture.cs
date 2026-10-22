using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3104: a string prefix or suffix check that compares with the player's culture, as in
    // "id.StartsWith("boss")".
    //
    // StartsWith and EndsWith given a string use culture rules, so ignorable characters and
    // language-specific letter matching can change the answer between PCs, and they run slower than
    // a plain character compare. Pass StringComparison.Ordinal, or OrdinalIgnoreCase.
    //
    // Checked: string StartsWith and EndsWith given a string, without a StringComparison or culture,
    // or with a null culture, which falls back to the player's.
    // The char overloads already compare ordinally. Reported on the method name. Game code only.
    //
    // Calls through "?." count too, as in "name?.StartsWith("a")".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PrefixOrSuffixCheckWithCultureAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3104",
            "Prefix or suffix check that depends on the player's culture",
            "'{0}' compares with the player's culture, so the result can change with their language settings; pass StringComparison.Ordinal",
            "Culture",
            CodeKind.Game,
            sourceRule: "CA1310");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                SimpleNameSyntax name = CultureCalls.CalledName(invocation);
                if (name == null || (name.Identifier.ValueText != "StartsWith" && name.Identifier.ValueText != "EndsWith")) return;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation)) return;
                IMethodSymbol method = operation.TargetMethod;
                if (method.ContainingType.SpecialType != SpecialType.System_String || method.Parameters.Length == 0) return;
                if (method.Parameters[0].Type.SpecialType != SpecialType.System_String) return;
                if (CultureCalls.TakesComparison(method) && !CultureCalls.PassesNullCulture(operation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), method.Name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}