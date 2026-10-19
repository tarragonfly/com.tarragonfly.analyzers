using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2009: a cast from a class to an interface that neither the class nor any class derived from it
    // in the same assembly implements, as in "(IDamageable)pickup".
    //
    // The cast compiles, because a subclass could implement the interface, but with no such subclass
    // it always throws InvalidCastException.
    //
    // Only classes declared in the assembly being compiled are checked, since only their subclasses are
    // all known. Reported on the target type.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImpossibleInterfaceCastAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2009",
            "Cast that always fails",
            "No '{0}' in this assembly implements '{1}', so this cast always throws",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            Compilation compilation = start.Compilation;
            Lazy<List<INamedTypeSymbol>> types = new Lazy<List<INamedTypeSymbol>>(() => AllTypes(compilation.Assembly.GlobalNamespace), LazyThreadSafetyMode.ExecutionAndPublication);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                CastExpressionSyntax cast = (CastExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type is INamedTypeSymbol target) || target.TypeKind != TypeKind.Interface) return;
                if (!(context.SemanticModel.GetTypeInfo(cast.Expression, context.CancellationToken).Type is INamedTypeSymbol source) || source.TypeKind != TypeKind.Class) return;
                if (!SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, compilation.Assembly)) return;
                if (Implements(source, target)) return;

                foreach (INamedTypeSymbol candidate in types.Value)
                {
                    if (DerivesFrom(candidate, source) && Implements(candidate, target)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, cast.Type.GetLocation(), source.Name, target.Name));
            }), SyntaxKind.CastExpression);
        }

        private static List<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
        {
            List<INamedTypeSymbol> types = new List<INamedTypeSymbol>();
            Stack<INamespaceOrTypeSymbol> pending = new Stack<INamespaceOrTypeSymbol>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                INamespaceOrTypeSymbol current = pending.Pop();
                if (current is INamedTypeSymbol type) types.Add(type);
                foreach (ISymbol member in current.GetMembers())
                {
                    if (member is INamespaceOrTypeSymbol child) pending.Push(child);
                }
            }
            return types;
        }

        private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol target)
        {
            foreach (INamedTypeSymbol candidate in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, target)) return true;
            }
            return false;
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (INamedTypeSymbol current = type.BaseType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            }
            return false;
        }
    }
}