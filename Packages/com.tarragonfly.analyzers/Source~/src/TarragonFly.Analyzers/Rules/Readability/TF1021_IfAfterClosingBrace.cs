using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1021: an if that starts on the same line as the closing brace before it, as in "} if (shielded)
    // {".
    //
    // It reads like an else if, but both conditions get checked; either "else" is missing or the if
    // belongs on its own line.
    //
    // "} else if" is fine. Reported on the if keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IfAfterClosingBraceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1021",
            "If on the same line as a closing brace",
            "This if follows a closing brace on the same line; add 'else' if it was meant, or start a new line",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                IfStatementSyntax ifStatement = (IfStatementSyntax)context.Node;
                if (!Branches.IsChainStart(ifStatement)) return;

                SyntaxToken previous = ifStatement.IfKeyword.GetPreviousToken();
                if (!previous.IsKind(SyntaxKind.CloseBraceToken)) return;
                int previousLine = previous.GetLocation().GetLineSpan().EndLinePosition.Line;
                int ifLine = ifStatement.IfKeyword.GetLocation().GetLineSpan().StartLinePosition.Line;
                if (previousLine == ifLine) context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation()));
            }), SyntaxKind.IfStatement);
        }
    }
}