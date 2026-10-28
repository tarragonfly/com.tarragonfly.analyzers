using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9006: a test method with a shape NUnit or the Unity Test Framework can't run.
    //
    // The Test Runner then marks it NotRunnable, skips it with a warning that's easy to miss, or, for
    // async void, reports a pass before the test has finished.
    //
    // Reported:
    //   - async void tests (return Task, or use [UnityTest] with UniTask.ToCoroutine());
    //   - UniTaskVoid tests: no GetAwaiter(), so the runner can't await them any more than async
    //     void (return Task);
    //   - tests that aren't public;
    //   - generic tests with no [TestCase] or [TestCaseSource] to work out the type from;
    //   - [UnityTest] methods that don't return IEnumerator;
    //   - [Test] or [TestCase] methods returning IEnumerator, which only [UnityTest] steps through;
    //   - other tests returning a value with no ExpectedResult; with [TestCaseSource] the source can
    //     set it, so those are fine;
    //   - local functions marked as tests, which the runner never finds.
    // Static tests are fine. Reported on the method name. Test code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnrunnableTestAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9006",
            "Test method the runner can't run",
            "{0}",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method) || !Frameworks.NUnit.IsTest(method)) return;
                string problem = Problem(method);
                if (problem != null) context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), problem));
            }), SyntaxKind.MethodDeclaration);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                LocalFunctionStatementSyntax local = (LocalFunctionStatementSyntax)context.Node;
                if (local.AttributeLists.Count == 0 || !(context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) is IMethodSymbol method) || !Frameworks.NUnit.IsTest(method)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, local.Identifier.GetLocation(), "The runner only finds test methods of a class, never local functions; move this out of the method"));
            }), SyntaxKind.LocalFunctionStatement);
        }

        private static string Problem(IMethodSymbol method)
        {
            if (method.IsAsync && method.ReturnsVoid) return "An async void test can finish before its awaits do; return Task instead";
            if (method.DeclaredAccessibility != Accessibility.Public) return "NUnit only runs public test methods; make this public";
            if (method.IsGenericMethod && !SymbolNames.HasAttribute(method, "NUnit.Framework.TestCaseAttribute") && !SymbolNames.HasAttribute(method, "NUnit.Framework.TestCaseSourceAttribute"))
            {
                return "NUnit can't work out the type arguments of this generic test; add [TestCase] arguments or make it non-generic";
            }

            bool returnsEnumerator = SymbolNames.IsNamed(method.ReturnType, "System.Collections.IEnumerator");
            if (Frameworks.NUnit.IsUnityTest(method)) return returnsEnumerator ? null : "A [UnityTest] must return IEnumerator";
            if (returnsEnumerator) return "Only [UnityTest] steps through an IEnumerator; use [UnityTest] instead of [Test]";
            if (UniTask.IsUniTaskVoid(method.ReturnType)) return "The runner can't await a UniTaskVoid, so it can't tell when this test finishes; return Task instead";
            bool expected = Frameworks.NUnit.HasExpectedResult(method);
            if (!Frameworks.NUnit.ReturnsValue(method)) return expected ? "This test has no result to compare with ExpectedResult; return a value or remove ExpectedResult" : null;
            if (expected) return null;
            if (SymbolNames.HasAttribute(method, "NUnit.Framework.TestCaseSourceAttribute")) return null;
            return "NUnit refuses a test that returns a value without ExpectedResult; return void or set ExpectedResult";
        }
    }
}
