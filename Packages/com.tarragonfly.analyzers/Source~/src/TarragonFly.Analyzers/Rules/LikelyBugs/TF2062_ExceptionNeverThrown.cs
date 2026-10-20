using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2062: an exception created as a statement on its own, as in "new InvalidOperationException("No
    // save slot");", with the throw missing.
    //
    // Nothing happens: the check meant to stop bad state does nothing and the code carries on.
    //
    // Reported on the "new" expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ExceptionNeverThrownAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2062",
            "Exception created but never thrown",
            "This exception is created and dropped; add 'throw'",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionStatementSyntax statement = (ExpressionStatementSyntax)context.Node;
                if (!(statement.Expression is BaseObjectCreationExpressionSyntax creation)) return;
                if (SymbolNames.IsOrDerivesFrom(context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type, "System.Exception"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation()));
                }
            }), SyntaxKind.ExpressionStatement);
        }
    }
}