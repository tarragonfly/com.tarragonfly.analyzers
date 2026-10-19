using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.UnityApi
{
    // TF4100: a new lambda handed to a UI Toolkit Unregister...Callback method, or removed from a
    // delegate field or property with -=, as in
    // "button.UnregisterCallback<ClickEvent>(evt => OnPlay());".
    //
    // Each lambda is a new delegate, so it never matches the one registered earlier and nothing is
    // removed. The callback keeps firing after the view closes, often into a destroyed object, and
    // registering again on every open adds another copy each time. Keep the delegate in a field, or
    // register and unregister a named method.
    //
    // Checked: methods in UnityEngine.UIElements whose name starts with "Unregister" and ends with
    // "Callback" (UnregisterCallback, UnregisterValueChangedCallback and the like), and -= on a
    // delegate-typed field, property or local. -= on an event is TF2505's job. Reported from the
    // lambda's start to its arrow, or from -= to the end of the lambda.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnregisterNewLambdaAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF4100",
            "New lambda unregistered from a callback",
            "A new lambda never matches the registered one, so this removes nothing; unregister a stored delegate or a named method",
            "Unity",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!method.Name.StartsWith("Unregister", StringComparison.Ordinal) || !method.Name.EndsWith("Callback", StringComparison.Ordinal)) return;
                IMethodSymbol original = method.ReducedFrom ?? method;
                if (original.ContainingType == null || !SymbolNames.IsInNamespace(original.ContainingType, "UnityEngine.UIElements")) return;

                foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
                {
                    if (!(argument.Expression is AnonymousFunctionExpressionSyntax lambda)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(lambda.SyntaxTree, TextSpan.FromBounds(lambda.SpanStart, LambdaHeadEnd(lambda)))));
                }
            }), SyntaxKind.InvocationExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (!(assignment.Right is AnonymousFunctionExpressionSyntax)) return;
                ISymbol target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
                if (target is IEventSymbol || target == null) return;
                ITypeSymbol type = context.SemanticModel.GetTypeInfo(assignment.Left, context.CancellationToken).Type;
                if (type?.TypeKind != TypeKind.Delegate) return;
                TextSpan span = TextSpan.FromBounds(assignment.OperatorToken.SpanStart, assignment.Span.End);
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(assignment.SyntaxTree, span)));
            }), SyntaxKind.SubtractAssignmentExpression);
        }

        private static int LambdaHeadEnd(AnonymousFunctionExpressionSyntax lambda)
        {
            switch (lambda)
            {
                case SimpleLambdaExpressionSyntax simple: return simple.ArrowToken.Span.End;
                case ParenthesizedLambdaExpressionSyntax parenthesized: return parenthesized.ArrowToken.Span.End;
                case AnonymousMethodExpressionSyntax anonymous: return (anonymous.ParameterList?.Span ?? anonymous.DelegateKeyword.Span).End;
                default: return lambda.Span.End;
            }
        }
    }
}