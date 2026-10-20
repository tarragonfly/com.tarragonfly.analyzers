using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9002: a test that never checks anything, as in a [Test] that only calls "inventory.Add(sword)".
    //
    // It passes as long as nothing throws, so it keeps passing when the behaviour it was written for
    // breaks.
    //
    // A test checks something when it, or a method it calls in this assembly, calls a method whose
    // name has the word assert, check, expect, must, should, validate or verify (AssertHealth, PreCheck
    // or assert_value, but not Checkpoint or Expectation), calls any method of a framework type named
    // like Assert (NUnit's Assert, CollectionAssert and StringAssert, Unity's LogAssert), or throws an
    // exception whose type name contains assert. A delegate counts through its own name, as in
    // "check()" or "verify.Invoke()", and a dynamic call counts too, since it might be one. Helper
    // classes in the project count only through their method names, not their class or receiver
    // names. Tests are [Test], [TestCase], [TestCaseSource], [Theory] and [UnityTest] methods, ignored
    // ones included. Tests with an ExpectedResult are skipped, since NUnit compares it with what they
    // return, and so are [TestCaseSource] tests returning a value, whose source data can set one.
    // Reported on the test name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TestWithoutAssertionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9002",
            "Test without an assertion",
            "'{0}' never checks a result, so it keeps passing when the behaviour breaks; add an assertion",
            "Tests",
            CodeKind.Tests,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        private static readonly string[] AssertionWords = { "assert", "check", "expect", "must", "should", "validate", "verify" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<IMethodSymbol, AssertsAndCalls> methods = new ConcurrentDictionary<IMethodSymbol, AssertsAndCalls>(SymbolEqualityComparer.Default);
            ConcurrentBag<(IMethodSymbol Test, Location Name)> tests = new ConcurrentBag<(IMethodSymbol, Location)>();

            // Every method body is read, analyzed file or not, since a test may rely on a helper anywhere in the assembly.
            start.RegisterSyntaxNodeAction(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                SyntaxNode body = (SyntaxNode)declaration.Body ?? declaration.ExpressionBody;
                if (body == null || !(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                methods[method] = Read(context.SemanticModel, body, context.CancellationToken);
                if (Frameworks.NUnit.IsTest(method) && !ReturnsExpectedResult(method) && scope.IsAnalyzed(declaration.SyntaxTree)) tests.Add((method, declaration.Identifier.GetLocation()));
            }, SyntaxKind.MethodDeclaration);

            start.RegisterCompilationEndAction(context =>
            {
                foreach ((IMethodSymbol test, Location name) in tests)
                {
                    if (!AssertsSomewhere(test, methods, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))) context.ReportDiagnostic(Diagnostic.Create(Rule, name, test.Name));
                }
            });
        }

        private static AssertsAndCalls Read(SemanticModel model, SyntaxNode body, CancellationToken ct)
        {
            AssertsAndCalls checks = new AssertsAndCalls();
            foreach (SyntaxNode node in body.DescendantNodesAndSelf())
            {
                if (node is InvocationExpressionSyntax invocation)
                {
                    SymbolInfo info = model.GetSymbolInfo(invocation, ct);
                    if (model.GetTypeInfo(invocation, ct).Type?.TypeKind == TypeKind.Dynamic)
                    {
                        checks.Asserts = true;
                        continue;
                    }
                    if (!((info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null)) is IMethodSymbol called)) continue;
                    bool frameworkAssert = !called.ContainingType.Locations.Any(location => location.IsInSource) && called.ContainingType.Name.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0;
                    string name = called.MethodKind == MethodKind.DelegateInvoke ? DelegateName(invocation) ?? called.Name : called.Name;
                    if (frameworkAssert || LooksLikeAssertion(name)) checks.Asserts = true;
                    else if (called.Locations.Length > 0 && called.Locations[0].IsInSource) checks.Calls.Add((called.ReducedFrom ?? called).OriginalDefinition);
                }
                else if (node is ThrowStatementSyntax || node is ThrowExpressionSyntax)
                {
                    ExpressionSyntax thrown = node is ThrowStatementSyntax statement ? statement.Expression : ((ThrowExpressionSyntax)node).Expression;
                    string type = thrown == null ? null : model.GetTypeInfo(thrown, ct).Type?.Name;
                    if (type != null && type.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0) checks.Asserts = true;
                }
            }
            return checks;
        }

        // One of the words of a name split at underscores and case changes, as AssertThat into Assert
        // and That, or IMust into I and Must.
        private static bool LooksLikeAssertion(string name)
        {
            int start = 0;
            for (int i = 1; i <= name.Length; i++)
            {
                if (i < name.Length && !StartsWord(name, i)) continue;
                string word = name.Substring(start, i - start).Trim('_');
                if (AssertionWords.Any(assertion => string.Equals(word, assertion, StringComparison.OrdinalIgnoreCase))) return true;
                start = i;
            }
            return false;
        }

        private static bool StartsWord(string name, int i)
        {
            char previous = name[i - 1];
            char current = name[i];
            if (current == '_' || previous == '_') return true;
            if (char.IsLower(previous) && char.IsUpper(current)) return true;
            return char.IsUpper(previous) && char.IsUpper(current) && i + 1 < name.Length && char.IsLower(name[i + 1]);
        }

        // The variable a delegate is called through: "check" in check() and check.Invoke().
        private static string DelegateName(InvocationExpressionSyntax invocation)
        {
            ExpressionSyntax target = invocation.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Invoke" ? access.Expression : invocation.Expression;
            if (target is MemberAccessExpressionSyntax member) target = member.Name;
            return target is IdentifierNameSyntax identifier ? identifier.Identifier.ValueText : null;
        }

        // NUnit compares what the test returns with its ExpectedResult, set on [Test] or [TestCase] or
        // in the [TestCaseSource] data. For the source data, awaitable tasks and [UnityTest]
        // enumerators don't count as returned values.
        private static bool ReturnsExpectedResult(IMethodSymbol test)
        {
            if (test.GetAttributes().Any(attribute => attribute.NamedArguments.Any(argument => argument.Key == "ExpectedResult"))) return true;
            if (test.ReturnsVoid || Frameworks.NUnit.IsUnityTest(test) || UniTask.IsAwaitable(test.ReturnType)) return false;
            return !SymbolNames.IsNamed(test.ReturnType, "System.Collections.IEnumerator") && SymbolNames.HasAttribute(test, "NUnit.Framework.TestCaseSourceAttribute");
        }

        private static bool AssertsSomewhere(IMethodSymbol method, ConcurrentDictionary<IMethodSymbol, AssertsAndCalls> methods, HashSet<IMethodSymbol> visited)
        {
            if (!methods.TryGetValue(method, out AssertsAndCalls checks)) return false;
            if (checks.Asserts) return true;
            foreach (IMethodSymbol called in checks.Calls)
            {
                if (visited.Add(called) && AssertsSomewhere(called, methods, visited)) return true;
            }
            return false;
        }

        private sealed class AssertsAndCalls
        {
            public bool Asserts;
            public readonly List<IMethodSymbol> Calls = new List<IMethodSymbol>();
        }
    }
}