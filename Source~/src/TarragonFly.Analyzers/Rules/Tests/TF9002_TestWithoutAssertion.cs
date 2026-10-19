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

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9002: a test that never checks anything, as in a [Test] that only calls "inventory.Add(sword)".
    //
    // It passes as long as nothing throws, so it keeps passing when the behaviour it was written for
    // breaks.
    //
    // A test checks something when it, or a method it calls in this assembly, calls a method whose
    // name contains assert, check, expect, must, should, validate or verify, or any method of a
    // framework type named like Assert (NUnit's Assert, CollectionAssert and StringAssert, Unity's
    // LogAssert), or throws an exception whose type name contains assert. Helper classes in the
    // project only count through their method names. [Test], [TestCase],
    // [TestCaseSource], [Theory] and [UnityTest] methods count. Reported on the test name.
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
                if (Frameworks.NUnit.IsTest(method) && scope.IsAnalyzed(declaration.SyntaxTree)) tests.Add((method, declaration.Identifier.GetLocation()));
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
                    if (!((info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null)) is IMethodSymbol called)) continue;
                    bool frameworkAssert = !called.ContainingType.Locations.Any(location => location.IsInSource) && called.ContainingType.Name.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (frameworkAssert || LooksLikeAssertion(called.Name)) checks.Asserts = true;
                    else if (called.Locations.Length > 0 && called.Locations[0].IsInSource) checks.Calls.Add((called.ReducedFrom ?? called).OriginalDefinition);
                }
                else if (node is ThrowStatementSyntax || node is ThrowExpressionSyntax)
                {
                    ExpressionSyntax thrown = node is ThrowStatementSyntax statement ? statement.Expression : ((ThrowExpressionSyntax)node).Expression;
                    if (thrown is ObjectCreationExpressionSyntax creation && LooksLikeAssertion(model.GetTypeInfo(creation, ct).Type?.Name ?? string.Empty)) checks.Asserts = true;
                }
            }
            return checks;
        }

        private static bool LooksLikeAssertion(string name)
        {
            foreach (string word in AssertionWords)
            {
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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