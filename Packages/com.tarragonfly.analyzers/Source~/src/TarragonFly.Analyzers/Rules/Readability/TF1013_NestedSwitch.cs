using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1013: a switch statement inside another switch statement, lambdas included.
    //
    // Two levels of case labels are hard to follow; the inner switch usually belongs in its own
    // method, such as one per state of a state machine.
    //
    // Switch expressions are not checked, inside or out. Reported on the inner switch keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NestedSwitchAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1013",
            "Nested switch statement",
            "Move this nested switch into its own method",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SwitchStatementSyntax inner = (SwitchStatementSyntax)context.Node;
                for (SyntaxNode ancestor = inner.Parent; ancestor != null && !(ancestor is MemberDeclarationSyntax); ancestor = ancestor.Parent)
                {
                    if (!(ancestor is SwitchStatementSyntax)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, inner.SwitchKeyword.GetLocation()));
                    return;
                }
            }), SyntaxKind.SwitchStatement);
        }
    }
}