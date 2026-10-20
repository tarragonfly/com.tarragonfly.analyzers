using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2042: subtracting a delegate built in the expression itself, as in "all - (onHit + onDeath)" or
    // "onScore -= () => AddPoints()".
    //
    // A combined pair is only removed if it sits in exactly that order next to each other in the
    // list, and a lambda made on the spot never matches the one added earlier; then nothing is
    // removed and the handlers keep running. Remove stored delegates or method groups one at a time.
    //
    // Subtracting a name or member access (a variable, field, property or method group) is fine;
    // anything else counts, including casts, "new Action(...)" and call results. Both "-" and "-="
    // count, but "-=" on an event is left to TF2505. Reported on the whole subtraction.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DelegateChainSubtractionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2042",
            "Subtracting a combined delegate",
            "This only removes the handlers if they sit together in this order; remove them one at a time",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax right = context.Node is BinaryExpressionSyntax binary ? binary.Right : ((AssignmentExpressionSyntax)context.Node).Right;
                while (right is ParenthesizedExpressionSyntax parenthesized) right = parenthesized.Expression;
                if (right is SimpleNameSyntax || right is MemberAccessExpressionSyntax) return;
                if (context.Node is AssignmentExpressionSyntax assignment
                    && context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is IEventSymbol) return;
                if (context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type?.TypeKind == TypeKind.Delegate)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
                }
            }), SyntaxKind.SubtractExpression, SyntaxKind.SubtractAssignmentExpression);
        }
    }
}