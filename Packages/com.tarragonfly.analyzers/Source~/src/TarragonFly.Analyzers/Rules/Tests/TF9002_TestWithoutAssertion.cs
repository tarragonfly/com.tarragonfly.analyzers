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
using Microsoft.CodeAnalysis.Operations;
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
    // Uncalled lambdas and local functions don't count. Inline callbacks count when a source helper
    // calls that parameter, or when the test returns UniTask.ToCoroutine, or a source helper that
    // returns it, or awaits Task.Run. A void test's arrow body throws its result away.
    // Local delegates and tasks are followed through initializers and earlier straight-line
    // assignments. Branching writes leave their value unknown.
    // Callbacks handed to an unknown method in another assembly may run there and count too. This
    // stays conservative across assembly boundaries; known deferred APIs are skipped.
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
                SyntaxNode body = context.Node is MethodDeclarationSyntax declaration ? (SyntaxNode)declaration.Body ?? declaration.ExpressionBody
                    : (SyntaxNode)((LocalFunctionStatementSyntax)context.Node).Body ?? ((LocalFunctionStatementSyntax)context.Node).ExpressionBody;
                if (body == null || !(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol method)) return;
                AssertsAndCalls checks = Read(context.SemanticModel, body, context.CancellationToken, Frameworks.NUnit.IsUnityTest(method), returnsVoid: method.ReturnsVoid);
                RecordUsedCallbacks(context.SemanticModel, method, body, checks, context.CancellationToken);
                methods[method] = checks;
                if (context.Node is MethodDeclarationSyntax test && Frameworks.NUnit.IsTest(method) && !ReturnsExpectedResult(method) && scope.IsAnalyzed(test.SyntaxTree)) tests.Add((method, test.Identifier.GetLocation()));
            }, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);

            start.RegisterCompilationEndAction(context =>
            {
                foreach ((IMethodSymbol test, Location name) in tests)
                {
                    if (!AssertsSomewhere(test, methods, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default))) context.ReportDiagnostic(Diagnostic.Create(Rule, name, test.Name));
                }
            });
        }

        private static AssertsAndCalls Read(SemanticModel model, SyntaxNode body, CancellationToken ct, bool unityTest = false, bool observedTask = false, bool returnsVoid = false)
        {
            AssertsAndCalls checks = new AssertsAndCalls();
            foreach (SyntaxNode node in body.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax) && !(child is LocalFunctionStatementSyntax)))
            {
                ExpressionSyntax observed = node is AwaitExpressionSyntax awaited ? awaited.Expression
                    : node is ReturnStatementSyntax returnedStatement ? returnedStatement.Expression
                    : returnsVoid ? null : (node as ArrowExpressionClauseSyntax)?.Expression;
                if (observed != null)
                {
                    AssertsAndCalls taskChecks = ReadObservedTask(model, model.GetOperation(observed, ct), ct);
                    if (taskChecks != null) checks.Callbacks.Add((null, 0, taskChecks, true));
                }
                if (node is BaseObjectCreationExpressionSyntax && model.GetOperation(node, ct) is IObjectCreationOperation creation && creation.Constructor != null)
                {
                    AddCallbacks(model, creation.Constructor, creation.Arguments, false, checks, ct);
                }
                else if (node is AssignmentExpressionSyntax && model.GetOperation(node, ct) is IEventAssignmentOperation subscription && subscription.Adds)
                {
                    // The event may be raised later in the test, so a handler counts as run.
                    AssertsAndCalls handler = ReadDelegate(model, subscription.HandlerValue, ct);
                    if (handler != null) checks.Callbacks.Add((null, 0, handler, true));
                }
                if (node is InvocationExpressionSyntax invocation)
                {
                    SymbolInfo info = model.GetSymbolInfo(invocation, ct);
                    if (model.GetTypeInfo(invocation, ct).Type?.TypeKind == TypeKind.Dynamic)
                    {
                        checks.Asserts = true;
                        continue;
                    }
                    if (!((info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null)) is IMethodSymbol called)) continue;
                    if (model.GetOperation(invocation, ct) is IInvocationOperation operation)
                    {
                        if (called.MethodKind == MethodKind.DelegateInvoke)
                        {
                            AssertsAndCalls immediate = ReadDelegate(model, LocalAssignments.InvokedDelegate(operation), ct);
                            if (immediate != null) checks.Callbacks.Add((null, 0, immediate, true));
                        }
                        bool returned = invocation.Parent is ReturnStatementSyntax or YieldStatementSyntax || invocation.Parent is ArrowExpressionClauseSyntax && !returnsVoid;
                        bool used = returned || invocation.Parent is AwaitExpressionSyntax or ArgumentSyntax
                            || invocation.Parent is MemberAccessExpressionSyntax receiver && receiver.Expression == invocation
                            || invocation.Parent is ForEachStatementSyntax loop && loop.Expression == invocation
                            || observedTask && invocation.Span == body.Span;
                        bool toCoroutine = called.Name == "ToCoroutine" && IsDeferringType(called.ContainingType);
                        bool runs = used && IsDeferringType(called.ContainingType) && (!toCoroutine || unityTest && returned);
                        // A helper's coroutine runs once a [UnityTest] returns it, which only the test knows.
                        if (toCoroutine && returned && !unityTest)
                        {
                            if (checks.ReturnedCoroutine == null) checks.ReturnedCoroutine = new AssertsAndCalls();
                            AddCallbacks(model, called, operation.Arguments, true, checks.ReturnedCoroutine, ct);
                        }
                        else AddCallbacks(model, called, operation.Arguments, runs, checks, ct);
                        if (unityTest && returned && called.Locations.Length > 0 && called.Locations[0].IsInSource) checks.ReturnsCoroutineFrom.Add(called.OriginalDefinition);
                    }
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

        private static void AddCallbacks(SemanticModel model, IMethodSymbol called, ImmutableArray<IArgumentOperation> arguments, bool runs, AssertsAndCalls checks, CancellationToken ct)
        {
            foreach (IArgumentOperation argument in arguments)
            {
                if (argument.Parameter == null) continue;
                // Each lambda in an expanded params Action[] is its own callback.
                IEnumerable<IOperation> values = argument.ArgumentKind == ArgumentKind.ParamArray && argument.Value is IArrayCreationOperation array && array.Initializer != null
                    ? array.Initializer.ElementValues : new[] { argument.Value };
                foreach (IOperation value in values)
                {
                    AssertsAndCalls callback = ReadDelegate(model, value, ct);
                    if (callback != null) checks.Callbacks.Add(((called.ReducedFrom ?? called).OriginalDefinition, argument.Parameter.Ordinal, callback, runs));
                }
            }
        }

        // The delegate parameters a method body uses in any way: calls, ?.Invoke(), passes on or stores.
        // A stored callback may run later, so only one the method never touches counts as unused.
        private static void RecordUsedCallbacks(SemanticModel model, IMethodSymbol method, SyntaxNode body, AssertsAndCalls checks, CancellationToken ct)
        {
            HashSet<string> names = new HashSet<string>(method.Parameters.Where(parameter => IsCallbackType(parameter.Type)).Select(parameter => parameter.Name));
            if (names.Count == 0) return;
            foreach (IdentifierNameSyntax name in body.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (names.Contains(name.Identifier.ValueText) && model.GetSymbolInfo(name, ct).Symbol is IParameterSymbol parameter
                    && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method)) checks.UsedCallbackParameters.Add(parameter.Ordinal);
            }
        }

        private static bool IsCallbackType(ITypeSymbol type)
        {
            return type?.TypeKind == TypeKind.Delegate || type is IArrayTypeSymbol array && array.ElementType.TypeKind == TypeKind.Delegate;
        }

        // Library calls that may hold a callback without running it: LINQ until enumerated, and task
        // factories until awaited, blocked on or chained.
        private static bool IsDeferringType(INamedTypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "System.Linq.Enumerable") || SymbolNames.IsNamed(type, "System.Linq.Queryable")
                || SymbolNames.IsNamed(type, "Cysharp.Threading.Tasks.UniTask") || SymbolNames.IsNamed(type, "System.Threading.Tasks.Task");
        }

        private static AssertsAndCalls ReadDelegate(SemanticModel model, IOperation value, CancellationToken ct)
        {
            AssertsAndCalls all = null;
            foreach (IOperation target in LocalAssignments.PossibleDelegates(model, value, ct))
            {
                AssertsAndCalls callback;
                if (target is IAnonymousFunctionOperation lambda) callback = Read(model, lambda.Body.Syntax, ct);
                else
                {
                    IMethodSymbol method = ((IMethodReferenceOperation)target).Method;
                    callback = new AssertsAndCalls();
                    callback.Asserts = LooksLikeAssertion(method.Name) || !method.ContainingType.Locations.Any(location => location.IsInSource)
                        && method.ContainingType.Name.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0;
                    callback.Calls.Add(method.OriginalDefinition);
                }
                if (all == null) all = callback;
                else
                {
                    all.Asserts |= callback.Asserts;
                    all.Calls.AddRange(callback.Calls);
                    all.Callbacks.AddRange(callback.Callbacks);
                }
            }
            return all;
        }

        private static AssertsAndCalls ReadObservedTask(SemanticModel model, IOperation value, CancellationToken ct)
        {
            if (!UniTask.IsTask(value?.Type) && !SymbolNames.IsNamed(value?.Type, "System.Runtime.CompilerServices.ConfiguredTaskAwaitable")
                && !SymbolNames.IsNamed(value?.Type, "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1")) return null;
            value = LocalAssignments.ResolveValue(model, value, ct);
            if (value is IInvocationOperation configure && configure.TargetMethod.Name == "ConfigureAwait" && UniTask.IsTask(configure.TargetMethod.ContainingType))
                value = LocalAssignments.ResolveValue(model, configure.Instance, ct);
            return value is IInvocationOperation call && call.TargetMethod.Name == "Run" && SymbolNames.IsNamed(call.TargetMethod.ContainingType, "System.Threading.Tasks.Task")
                ? Read(model, call.Syntax, ct, observedTask: true) : null;
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
        // in the [TestCaseSource] data. For the source data, tasks without a result, UniTaskVoid and [UnityTest]
        // enumerators don't count as returned values.
        private static bool ReturnsExpectedResult(IMethodSymbol test)
        {
            if (Frameworks.NUnit.HasExpectedResult(test)) return true;
            if (!Frameworks.NUnit.ReturnsValue(test) || Frameworks.NUnit.IsUnityTest(test) || UniTask.IsUniTaskVoid(test.ReturnType)) return false;
            return !SymbolNames.IsNamed(test.ReturnType, "System.Collections.IEnumerator") && SymbolNames.HasAttribute(test, "NUnit.Framework.TestCaseSourceAttribute");
        }

        private static bool AssertsSomewhere(IMethodSymbol method, ConcurrentDictionary<IMethodSymbol, AssertsAndCalls> methods, HashSet<IMethodSymbol> visited)
        {
            if (!methods.TryGetValue(method, out AssertsAndCalls checks)) return false;
            return AssertsIn(checks, methods, visited);
        }

        private static bool AssertsIn(AssertsAndCalls checks, ConcurrentDictionary<IMethodSymbol, AssertsAndCalls> methods, HashSet<IMethodSymbol> visited)
        {
            if (checks.Asserts) return true;
            foreach (IMethodSymbol called in checks.Calls)
            {
                if (visited.Add(called) && AssertsSomewhere(called, methods, visited)) return true;
            }
            foreach (IMethodSymbol helper in checks.ReturnsCoroutineFrom)
            {
                if (methods.TryGetValue(helper, out AssertsAndCalls helperChecks) && helperChecks.ReturnedCoroutine != null
                    && AssertsIn(helperChecks.ReturnedCoroutine, methods, visited)) return true;
            }
            foreach ((IMethodSymbol method, int parameterIndex, AssertsAndCalls body, bool runs) in checks.Callbacks)
            {
                // A method read above has to use the parameter. Constructors and code that wasn't read,
                // here or in another assembly, may run it unless it's a known deferring API.
                bool invoked = runs || method != null && (methods.TryGetValue(method, out AssertsAndCalls callee)
                    ? callee.UsedCallbackParameters.Contains(parameterIndex) : !IsDeferringType(method.ContainingType));
                if (invoked && AssertsIn(body, methods, visited)) return true;
            }
            return false;
        }

        private sealed class AssertsAndCalls
        {
            public bool Asserts;
            public readonly List<IMethodSymbol> Calls = new List<IMethodSymbol>();
            public readonly HashSet<int> UsedCallbackParameters = new HashSet<int>();
            // Callbacks of the UniTask.ToCoroutine this method returns, and the source methods whose
            // returned coroutine this [UnityTest] hands back to Unity.
            public AssertsAndCalls ReturnedCoroutine;
            public readonly List<IMethodSymbol> ReturnsCoroutineFrom = new List<IMethodSymbol>();
            public readonly List<(IMethodSymbol Method, int ParameterIndex, AssertsAndCalls Body, bool Runs)> Callbacks = new List<(IMethodSymbol, int, AssertsAndCalls, bool)>();
        }
    }
}
