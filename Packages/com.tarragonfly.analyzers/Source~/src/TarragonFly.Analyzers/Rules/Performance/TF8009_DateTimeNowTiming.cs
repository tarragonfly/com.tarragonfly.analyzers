using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8009: elapsed time measured with DateTime.Now, as in "DateTime.Now - loadStarted".
    //
    // DateTime.Now is wall-clock time: it is coarse, does a time-zone lookup on every read and jumps
    // when the clock changes. Use Stopwatch, or Time.realtimeSinceStartupAsDouble in a game.
    //
    // Checked: "DateTime.Now - x" and "DateTime.Now.Subtract(x)" where x is a DateTime. Subtracting a
    // TimeSpan, or reading DateTime.Now for display, is fine. Reported on the subtraction, or on the
    // Subtract call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DateTimeNowTimingAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8009",
            "DateTime.Now used to measure time",
            "Measure elapsed time with Stopwatch or Time.realtimeSinceStartupAsDouble; DateTime.Now is coarse and jumps with the clock",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax subtraction = (BinaryExpressionSyntax)context.Node;
                if (IsNow(context, subtraction.Left) && IsDateTime(context, subtraction.Right))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, subtraction.GetLocation()));
                }
            }), SyntaxKind.SubtractExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "Subtract" || invocation.ArgumentList.Arguments.Count != 1) return;
                if (IsNow(context, access.Expression) && IsDateTime(context, invocation.ArgumentList.Arguments[0].Expression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation()));
                }
            }), SyntaxKind.InvocationExpression);
        }

        private static bool IsNow(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            return context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is IPropertySymbol property
                && property.Name == "Now" && SymbolNames.IsNamed(property.ContainingType, "System.DateTime");
        }

        private static bool IsDateTime(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            return SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type, "System.DateTime");
        }
    }
}