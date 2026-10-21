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
    // TF9007: a TestBench started in a test class that nothing disposes, as in
    // "TestBench.Start(out _bench).AddBoard(out _board);" in SetUp with no TearDown calling
    // _bench.Dispose().
    //
    // The bench owns every object and asset the chain spawned and every AtTearDown undo. Without
    // the Dispose they stay in the scene for the next test, which then finds two boards, or keeps
    // the pinned time scale.
    //
    // Checked: calls to TestBench.Start of TarragonFly.TestBench, found by name, so the rule is
    // silent in projects without the package. The bench is the out argument. Written to a field,
    // it needs a [TearDown], [UnityTearDown] or [OneTimeTearDown] method of the class or a base
    // class in this assembly that calls Dispose() on that field, itself or through a method it
    // calls. Written to a local, it needs a Dispose() call in the same method. Passed as "out _"
    // nothing can dispose it. A bench handed to a property, a parameter or a field from another
    // assembly isn't followed. Reported on the Start call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TestBenchNeverDisposedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9007",
            "TestBench started but never disposed",
            "'{0}' is started but nothing disposes it; add a TearDown that calls Dispose()",
            "Tests",
            CodeKind.Tests,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        private const string TestBenchType = "TarragonFly.TestBench.TestBench";

        private static readonly string[] TearDownAttributes =
        {
            "NUnit.Framework.TearDownAttribute",
            "NUnit.Framework.OneTimeTearDownAttribute",
            "UnityEngine.TestTools.UnityTearDownAttribute"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<IMethodSymbol, DisposesAndCalls> methods = new ConcurrentDictionary<IMethodSymbol, DisposesAndCalls>(SymbolEqualityComparer.Default);
            ConcurrentBag<BenchInField> benches = new ConcurrentBag<BenchInField>();

            // Every method body is read, analyzed file or not, since the TearDown may sit in a base class elsewhere in the assembly.
            start.RegisterSyntaxNodeAction(context =>
            {
                BaseMethodDeclarationSyntax declaration = (BaseMethodDeclarationSyntax)context.Node;
                SyntaxNode body = (SyntaxNode)declaration.Body ?? declaration.ExpressionBody;
                if (body == null || !(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                methods[method] = Read(context, body, method, benches, scope.IsAnalyzed(declaration.SyntaxTree));
            }, SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (BenchInField bench in benches)
                {
                    if (!DisposedInTearDown(bench, methods)) context.ReportDiagnostic(Diagnostic.Create(Rule, bench.StartCall, bench.Field.Name));
                }
            });
        }

        // Reports benches a local or a discard loses right away; benches in fields wait for the end,
        // when every TearDown has been read.
        private static DisposesAndCalls Read(SyntaxNodeAnalysisContext context, SyntaxNode body, IMethodSymbol method, ConcurrentBag<BenchInField> benches, bool analyzed)
        {
            DisposesAndCalls record = new DisposesAndCalls();
            foreach (InvocationExpressionSyntax invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called)) continue;
                if (called.Name == "Start" && SymbolNames.IsNamed(called.ContainingType, TestBenchType))
                {
                    if (analyzed) CheckStart(context, invocation, body, method.ContainingType, benches);
                    continue;
                }
                ISymbol disposed = DisposedHolder(invocation, context.SemanticModel, context.CancellationToken);
                if (disposed is IFieldSymbol field) record.DisposedFields.Add(field);
                else if (disposed == null && called.Locations.Any(location => location.IsInSource)) record.Calls.Add(called.OriginalDefinition);
            }
            return record;
        }

        private static void CheckStart(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax start, SyntaxNode body, INamedTypeSymbol type, ConcurrentBag<BenchInField> benches)
        {
            ISymbol holder = BenchHolder(start, context.SemanticModel, context.CancellationToken, out bool discarded);
            string undisposed = null;
            if (discarded) undisposed = "TestBench";
            else if (holder is IFieldSymbol field && field.DeclaringSyntaxReferences.Length > 0) benches.Add(new BenchInField(field, type, start.GetLocation()));
            else if (holder is ILocalSymbol local && !DisposedIn(body, local, context.SemanticModel, context.CancellationToken)) undisposed = local.Name;
            if (undisposed != null) context.ReportDiagnostic(Diagnostic.Create(Rule, start.GetLocation(), undisposed));
        }

        // The field or local the out argument writes the bench to; null when it is discarded or
        // goes somewhere the rule doesn't follow.
        private static ISymbol BenchHolder(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken ct, out bool discarded)
        {
            discarded = false;
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) continue;
                if (argument.Expression is DeclarationExpressionSyntax declared)
                {
                    discarded = declared.Designation is DiscardDesignationSyntax;
                    return declared.Designation is SingleVariableDesignationSyntax variable ? model.GetDeclaredSymbol(variable, ct) : null;
                }
                ISymbol target = model.GetSymbolInfo(argument.Expression, ct).Symbol;
                discarded = target is IDiscardSymbol;
                return target;
            }
            return null;
        }

        private static bool DisposedIn(SyntaxNode body, ILocalSymbol local, SemanticModel model, CancellationToken ct)
        {
            return body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(invocation => SymbolEqualityComparer.Default.Equals(DisposedHolder(invocation, model, ct), local));
        }

        // What "holder.Dispose()", "this.holder.Dispose()" or "holder?.Dispose()" disposes; null for any other call.
        private static ISymbol DisposedHolder(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken ct)
        {
            if (invocation.ArgumentList.Arguments.Count > 0) return null;
            ExpressionSyntax receiver;
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax access when access.Name.Identifier.ValueText == "Dispose":
                    receiver = access.Expression;
                    break;
                case MemberBindingExpressionSyntax binding when binding.Name.Identifier.ValueText == "Dispose":
                    receiver = invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression;
                    break;
                default:
                    return null;
            }
            return receiver == null ? null : model.GetSymbolInfo(receiver, ct).Symbol;
        }

        private static bool DisposedInTearDown(BenchInField bench, ConcurrentDictionary<IMethodSymbol, DisposesAndCalls> methods)
        {
            HashSet<IMethodSymbol> visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            for (INamedTypeSymbol current = bench.Type; current != null; current = current.BaseType)
            {
                foreach (IMethodSymbol method in current.GetMembers().OfType<IMethodSymbol>())
                {
                    if (TearDownAttributes.Any(attribute => SymbolNames.HasAttribute(method, attribute)) && Disposes(method, bench.Field, methods, visited)) return true;
                }
            }
            return false;
        }

        private static bool Disposes(IMethodSymbol method, IFieldSymbol field, ConcurrentDictionary<IMethodSymbol, DisposesAndCalls> methods, HashSet<IMethodSymbol> visited)
        {
            if (!visited.Add(method) || !methods.TryGetValue(method, out DisposesAndCalls record)) return false;
            if (record.DisposedFields.Contains(field, SymbolEqualityComparer.Default)) return true;
            return record.Calls.Any(called => Disposes(called, field, methods, visited));
        }

        private sealed class BenchInField
        {
            public BenchInField(IFieldSymbol field, INamedTypeSymbol type, Location startCall)
            {
                Field = field;
                Type = type;
                StartCall = startCall;
            }

            public IFieldSymbol Field { get; }
            public INamedTypeSymbol Type { get; }
            public Location StartCall { get; }
        }

        private sealed class DisposesAndCalls
        {
            public readonly List<IFieldSymbol> DisposedFields = new List<IFieldSymbol>();
            public readonly List<IMethodSymbol> Calls = new List<IMethodSymbol>();
        }
    }
}
