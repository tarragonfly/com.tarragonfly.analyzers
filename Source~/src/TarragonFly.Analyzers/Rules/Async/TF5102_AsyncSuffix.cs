using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5102: a method that returns UniTask, UniTaskVoid, Task or ValueTask without the "Async" suffix,
    // as in "UniTask FadeIn(CancellationToken ct)", or one named "...Async" that returns something
    // else, as in "void LoadAsync()".
    //
    // The suffix is how a caller knows to await the result or call Forget() on it; without it, a call
    // such as "FadeIn(ct);" looks finished when it has only started, and the fade then races whatever
    // comes next. A suffix on a method that isn't awaitable sends the caller looking for a task that
    // isn't there.
    //
    // UniTask, UniTask<T>, UniTaskVoid, Task, Task<T>, ValueTask and ValueTask<T> need the suffix. Any
    // return type with a GetAwaiter() method, and IAsyncEnumerable<T>, may carry it without needing
    // it. Fine without the suffix: overrides and interface implementations, whose name is set
    // elsewhere; Unity messages on MonoBehaviour, ScriptableObject, editor and other Unity types, such
    // as "async UniTaskVoid Start()"; test and setup methods; and Main. Local functions and lambdas
    // aren't checked. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncSuffixAnalyzer : ScopedAnalyzer
    {
        private const string Suffix = "Async";

        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5102",
            "Async suffix missing or misplaced",
            "{0}",
            "Async",
            CodeKind.All);

        private static readonly string[] TestSetupAttributes =
        {
            "NUnit.Framework.SetUpAttribute",
            "NUnit.Framework.TearDownAttribute",
            "NUnit.Framework.OneTimeSetUpAttribute",
            "NUnit.Framework.OneTimeTearDownAttribute",
            "UnityEngine.TestTools.UnitySetUpAttribute",
            "UnityEngine.TestTools.UnityTearDownAttribute"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                if (BaseMembers.IsOverrideOrImplementation(method)) return;

                bool named = method.Name.EndsWith(Suffix, StringComparison.Ordinal) && method.Name.Length > Suffix.Length;
                if (UniTask.IsAwaitable(method.ReturnType))
                {
                    if (named || IsNameFixedElsewhere(method)) return;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(),
                        $"'{method.Name}' returns {method.ReturnType.Name}; name it '{method.Name}{Suffix}' so callers know to await it or call Forget()"));
                }
                else if (named && !CanBeAwaited(method.ReturnType))
                {
                    string plain = method.Name.Substring(0, method.Name.Length - Suffix.Length);
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(),
                        $"'{method.Name}' can't be awaited; drop the suffix ('{plain}') or return UniTask"));
                }
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool IsNameFixedElsewhere(IMethodSymbol method)
        {
            if (method.Name == "Main" && method.IsStatic) return true;
            if (NUnit.IsTest(method) || TestSetupAttributes.Any(a => SymbolNames.HasAttribute(method, a))) return true;
            // Unity calls messages by name, so "Start" has to stay "Start" even when it awaits.
            return Frameworks.Unity.IsMessage(method);
        }

        private static bool CanBeAwaited(ITypeSymbol type)
        {
            if (type == null || type.SpecialType == SpecialType.System_Void) return false;
            if (SymbolNames.IsNamed(type.OriginalDefinition, "System.Collections.Generic.IAsyncEnumerable`1")) return true;
            for (ITypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (current.GetMembers("GetAwaiter").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0)) return true;
            }
            return false;
        }
    }
}