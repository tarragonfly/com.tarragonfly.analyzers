using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3004: a catch clause with an empty body, which swallows the exception.
    //
    // A failed save or asset load then leaves no trace in the Unity Console. Log it with
    // Debug.LogException, handle it, or say in a comment why ignoring it is safe.
    //
    // A comment in the body or a "when" filter means it was ignored on purpose. So does catching
    // OperationCanceledException or a type derived from it, such as TaskCanceledException, since that's
    // how a cancelled async wait ends; "catch (Exception) { }" and a type that only has Canceled in
    // its name are still reported. Reported on the whole catch clause. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IgnoredExceptionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3004",
            "Caught exception ignored",
            "This catch ignores the exception; log it with Debug.LogException, handle it, or comment why it's safe",
            "SilentFailures",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                CatchClauseSyntax catchClause = (CatchClauseSyntax)context.Node;
                BlockSyntax block = catchClause.Block;
                if (catchClause.Filter != null || block.Statements.Count > 0) return;
                if (catchClause.Declaration != null && context.SemanticModel.GetTypeInfo(catchClause.Declaration.Type, context.CancellationToken).Type is ITypeSymbol caught
                    && SymbolNames.IsOrDerivesFrom(caught, "System.OperationCanceledException")) return;
                if (Braces.HaveOnlyWhitespaceBetween(block.OpenBraceToken, block.CloseBraceToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, catchClause.GetLocation()));
                }
            }), SyntaxKind.CatchClause);
        }
    }
}