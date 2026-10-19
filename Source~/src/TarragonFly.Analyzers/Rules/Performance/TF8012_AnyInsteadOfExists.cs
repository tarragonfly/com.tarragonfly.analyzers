using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8012: Any(condition) on a List or an array, as in "enemies.Any(e => e.IsTargeting(player))".
    //
    // List.Exists and Array.Exists do the same check without allocating a LINQ enumerator.
    //
    // Any() without a condition, and other collection types, are fine, and equality checks such as
    // Any(id => id == wanted) are left to TF8018, which suggests Contains. Reported on the Any name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AnyInsteadOfExistsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8012",
            "Any where Exists works",
            "Use Exists instead of Any; it checks the {0} without allocating a LINQ enumerator",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 1 || LinqCalls.Method(context.SemanticModel, invocation, "Any", context.CancellationToken) == null) return;
                ITypeSymbol receiver = LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken);
                if (!LinqCalls.IsListOrArray(receiver) || AnyInsteadOfContainsAnalyzer.IsEqualityCheck(invocation, receiver)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), receiver is IArrayTypeSymbol ? "array" : "list"));
            }), SyntaxKind.InvocationExpression);
        }
    }
}