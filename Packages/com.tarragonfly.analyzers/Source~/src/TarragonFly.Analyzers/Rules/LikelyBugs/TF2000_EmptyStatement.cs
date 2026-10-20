using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2000: an empty statement, a lone ";".
    //
    // Usually harmless (a doubled ";;"), but "if (isGrounded);" or "foreach (var e in enemies);"
    // silently becomes the whole body and the block below runs every time.
    //
    // The bodies of for, while and do loops are skipped: "while (TryStep());" is a deliberate idiom.
    // Every other empty statement is reported, including after a label. Reported on the ";".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EmptyStatementAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2000",
            "Empty statement",
            "Remove this empty statement; after an if or foreach it silently becomes the whole body",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                EmptyStatementSyntax empty = (EmptyStatementSyntax)context.Node;
                SyntaxNode parent = empty.Parent;
                if (parent is ForStatementSyntax || parent is WhileStatementSyntax || parent is DoStatementSyntax) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, empty.SemicolonToken.GetLocation()));
            }), SyntaxKind.EmptyStatement);
        }
    }
}