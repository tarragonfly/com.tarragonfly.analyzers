using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2031: Stream.Read or ReadAsync called without using how many bytes it read.
    //
    // A read may return fewer bytes than asked, especially from files on device or network streams,
    // so the rest of the buffer holds old data and a save file loads corrupted.
    //
    // Calls on Stream and its subclasses count, awaited ones included. "_ = stream.Read(...)" says the
    // count is ignored on purpose and is fine. Reported on the call. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IgnoredStreamReadAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2031",
            "Stream.Read length ignored",
            "{0} can read fewer bytes than asked; use the count it returns",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax expression = ((ExpressionStatementSyntax)context.Node).Expression;
                if (expression is AwaitExpressionSyntax awaited) expression = awaited.Expression;
                if (!(expression is InvocationExpressionSyntax invocation)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if ((method.Name == "Read" || method.Name == "ReadAsync") && SymbolNames.IsOrDerivesFrom(method.ContainingType, "System.IO.Stream"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
                }
            }), SyntaxKind.ExpressionStatement);
        }
    }
}