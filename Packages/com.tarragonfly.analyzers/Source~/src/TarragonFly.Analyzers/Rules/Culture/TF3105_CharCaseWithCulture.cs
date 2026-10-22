using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3105: a single character's case changed with the player's culture, as in
    // "char.ToLower(key[0])".
    //
    // On a Turkish PC char.ToLower('I') gives a dotless i, so keys and IDs built from it stop
    // matching. Use char.ToLowerInvariant or char.ToUpperInvariant.
    //
    // Checked: char.ToUpper and char.ToLower given only the character. String ToUpper and ToLower
    // are TF3500's job. Reported on the method name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CharCaseWithCultureAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3105",
            "Character case changed with the player's culture",
            "'char.{0}' uses the player's culture, so 'I' becomes a dotless i on Turkish PCs; use char.{0}Invariant",
            "Culture",
            CodeKind.Game,
            sourceRule: "CA1304");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "ToUpper" && name != "ToLower") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (method.ContainingType.SpecialType != SpecialType.System_Char || method.Parameters.Length != 1) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation(), name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}