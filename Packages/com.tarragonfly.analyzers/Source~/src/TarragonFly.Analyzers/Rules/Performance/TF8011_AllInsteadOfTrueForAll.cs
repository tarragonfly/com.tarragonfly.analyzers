using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8011: All(condition) on a List or an array, as in "squad.All(unit => unit.IsReady)".
    //
    // List.TrueForAll and Array.TrueForAll do the same check without allocating a LINQ enumerator.
    //
    // Classes derived from List count too, called directly or through ?.. Other collection types
    // are fine. Reported on the All name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AllInsteadOfTrueForAllAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8011",
            "All where TrueForAll works",
            "Use TrueForAll instead of All; it checks the {0} without allocating a LINQ enumerator",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (LinqCalls.Method(context.SemanticModel, invocation, "All", context.CancellationToken) == null) return;
                ITypeSymbol receiver = LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken);
                if (!LinqCalls.IsListOrArray(receiver)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), receiver is IArrayTypeSymbol ? "array" : "list"));
            }), SyntaxKind.InvocationExpression);
        }
    }
}