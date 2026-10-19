using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9004: Thread.Sleep inside a test.
    //
    // A fixed wait makes the suite slow and still fails when the machine is busier than expected.
    // Wait for the condition itself, use "yield return null" frames in a [UnityTest], or await the
    // task under test.
    //
    // Calls in the test body and in lambdas inside it count, any overload; setup methods and helpers
    // aren't checked. [UnityTest] methods are checked too. Reported on the call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThreadSleepInTestAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9004",
            "Thread.Sleep in a test",
            "Wait for the condition instead of sleeping; a fixed Thread.Sleep slows the suite and still fails on a busy machine",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "Sleep") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !SymbolNames.IsNamed(method.ContainingType, "System.Threading.Thread")) return;
                MethodDeclarationSyntax declaration = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (declaration == null || !(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol test) || !Frameworks.NUnit.IsTest(test)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
            }), SyntaxKind.InvocationExpression);
        }
    }
}