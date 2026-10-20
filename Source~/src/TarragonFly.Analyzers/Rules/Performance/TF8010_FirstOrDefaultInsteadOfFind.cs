using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8010: FirstOrDefault(condition) on a List or an array, as in "enemies.FirstOrDefault(e =>
    // e.IsAlive)".
    //
    // LINQ allocates an enumerator and goes through interface calls; List.Find and Array.Find loop
    // over the items directly and allocate nothing, which adds up in per-frame code.
    //
    // Classes derived from List count too, called directly or through ?.. FirstOrDefault without a
    // condition, and other collection types, are fine. Reported on the FirstOrDefault name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FirstOrDefaultInsteadOfFindAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8010",
            "FirstOrDefault where Find works",
            "Use Find instead of FirstOrDefault; it loops over the {0} without allocating a LINQ enumerator",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 1 || LinqCalls.Method(context.SemanticModel, invocation, "FirstOrDefault", context.CancellationToken) == null) return;
                ITypeSymbol receiver = LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken);
                if (!LinqCalls.IsListOrArray(receiver)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), receiver is IArrayTypeSymbol ? "array" : "list"));
            }), SyntaxKind.InvocationExpression);
        }
    }
}