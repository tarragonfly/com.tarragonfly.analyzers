using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8017: LINQ's First() or Last() on a LinkedList, as in "path.Last()".
    //
    // Last() walks the whole list to reach the end, while the list's First and Last properties hold
    // both end nodes already.
    //
    // Only the overloads without a condition count. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LinqFirstOnLinkedListAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8017",
            "LINQ First or Last on a LinkedList",
            "Use the LinkedList's {0} property and its Value; {0}() goes through LINQ",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 0) return;
                IMethodSymbol method = LinqCalls.Method(context.SemanticModel, invocation, context.CancellationToken);
                if (method == null || (method.Name != "First" && method.Name != "Last")) return;
                ITypeSymbol receiver = LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken);
                if (!SymbolNames.IsNamed(receiver?.OriginalDefinition, "System.Collections.Generic.LinkedList`1")) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), method.Name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}