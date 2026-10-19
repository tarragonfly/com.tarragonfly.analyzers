using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2505: unsubscribing a lambda or anonymous method from an event, as in "player.Died -= () =>
    // ShowGameOver();".
    //
    // Each lambda is a new delegate, so it never matches the one subscribed earlier and nothing is
    // removed. The handler keeps firing, and in Unity it keeps the subscriber alive after it is
    // destroyed. Keep the delegate in a field, or subscribe a named method.
    //
    // Reported from the "-=" to the end of the lambda.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LambdaUnsubscribeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2505",
            "Lambda unsubscribed from an event",
            "A new lambda never matches the subscribed one, so this removes nothing; unsubscribe a stored delegate or a named method",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (!(assignment.Right is AnonymousFunctionExpressionSyntax)) return;
                if (context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is IEventSymbol)
                {
                    TextSpan span = TextSpan.FromBounds(assignment.OperatorToken.SpanStart, assignment.Span.End);
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(assignment.SyntaxTree, span)));
                }
            }), SyntaxKind.SubtractAssignmentExpression);
        }
    }
}