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
    // implements, as in "(IDamageable)pickup".
    //
    // The cast compiles, because a subclass could implement the interface, but with no such subclass
    // it always throws InvalidCastException.
    //
    // Only classes declared in the assembly being compiled are checked, and only when no other assembly
    // can subclass them: the class, and every class derived from it here, is not public (a containing
    // type counts) or has no constructor another assembly can call. A public class with a public or
    // protected constructor may have a subclass in another assembly that implements the interface, so
    // its casts aren't checked. (A sealed class never gets here: the compiler rejects the cast as
    // CS0030.) Reported on the target type.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ImpossibleInterfaceCastAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2009",
            "Cast that always fails",
            "No '{0}' implements '{1}', so this cast always throws",
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
                if (compilation.ClassifyCommonConversion(source, target).IsImplicit || CanBeSubclassedElsewhere(source)) return;

                foreach (INamedTypeSymbol candidate in types.Value)
                {
                    if (DerivesFrom(candidate, source) && (compilation.ClassifyCommonConversion(candidate, target).IsImplicit || CanBeSubclassedElsewhere(candidate))) return;
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

        // A public, unsealed class with a constructor another assembly can call: a subclass there may
        // implement the interface, and this assembly can't see it.
        private static bool CanBeSubclassedElsewhere(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                if (!IsVisibleOutsideTheAssembly(current.DeclaredAccessibility)) return false;
            }
            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (IsVisibleOutsideTheAssembly(constructor.DeclaredAccessibility)) return true;
            }
            return false;
        }

        // Protected counts: a subclass in another assembly can reach protected members.
        private static bool IsVisibleOutsideTheAssembly(Accessibility accessibility)
        {
            return accessibility == Accessibility.Public || accessibility == Accessibility.Protected || accessibility == Accessibility.ProtectedOrInternal;
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
