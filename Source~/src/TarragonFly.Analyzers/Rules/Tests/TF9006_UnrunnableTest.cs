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
    //   - tests that aren't public;
    //   - generic tests with no [TestCase] or [TestCaseSource] to work out the type from;
    //   - [UnityTest] methods that don't return IEnumerator;
    //   - [Test] or [TestCase] methods returning IEnumerator, which only [UnityTest] steps through;
    //   - other tests returning a value with no ExpectedResult.
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
            if (method.ReturnsVoid || UniTask.IsAwaitable(method.ReturnType) || HasExpectedResult(method)) return null;
            return "NUnit refuses a test that returns a value without ExpectedResult; return void or set ExpectedResult";
        }

        private static bool HasExpectedResult(IMethodSymbol method)
        {
            foreach (AttributeData attribute in method.GetAttributes())
            {
                foreach (System.Collections.Generic.KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
                {
                    if (argument.Key == "ExpectedResult") return true;
                }
            }
            return false;
        }
    }
}