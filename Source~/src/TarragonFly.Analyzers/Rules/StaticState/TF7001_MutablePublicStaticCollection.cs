using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7001: a public static collection anyone can change: an array field that isn't readonly, or a
    // field (readonly or not) of a mutable collection type such as List, Dictionary, HashSet, Queue,
    // Stack, IList, ICollection, ISet or IDictionary.
    //
    // "readonly" only stops the field being replaced, not its contents being edited, and with domain
    // reload off whatever was added stays there for the next Play Mode session.
    //
    // Fine: read-only types (IReadOnlyList, ReadOnlyCollection, ImmutableArray...), an interface field
    // holding a read-only wrapper such as Array.AsReadOnly(...), and fields of non-public types.
    // Reported on the field type. Game code only.
    //
    // LinkedList and the sorted collections count too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MutablePublicStaticCollectionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7001",
            "Mutable public static collection",
            "Any code can change the contents of '{0}', and with domain reload off they carry over into the next Play Mode session; expose a read-only view instead",
            "StaticState",
            CodeKind.Game);

        private static readonly string[] MutableCollections =
        {
            "System.Collections.Generic.List`1", "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.Queue`1", "System.Collections.Generic.Stack`1", "System.Collections.Generic.LinkedList`1",
            "System.Collections.Generic.SortedSet`1", "System.Collections.Generic.SortedDictionary`2", "System.Collections.Generic.SortedList`2",
            "System.Collections.Generic.IList`1", "System.Collections.Generic.ICollection`1", "System.Collections.Generic.ISet`1",
            "System.Collections.Generic.IDictionary`2", "System.Collections.ArrayList", "System.Collections.Hashtable",
            "System.Collections.IList", "System.Collections.IDictionary"
        };

        private static readonly string[] ReadOnlyWrappers =
        {
            "System.Collections.ObjectModel.ReadOnlyCollection`1", "System.Collections.ObjectModel.ReadOnlyDictionary`2"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax declaration = (FieldDeclarationSyntax)context.Node;
                VariableDeclaratorSyntax first = declaration.Declaration.Variables[0];
                if (!(context.SemanticModel.GetDeclaredSymbol(first, context.CancellationToken) is IFieldSymbol field)) return;
                if (!field.IsStatic || field.IsConst || !IsPublicEverywhere(field)) return;

                ITypeSymbol type = field.Type;
                bool mutable = type is IArrayTypeSymbol ? !field.IsReadOnly : IsMutableCollection(type);
                if (!mutable || HoldsReadOnlyWrapper(context, first)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Declaration.Type.GetLocation(), field.Name));
            }), SyntaxKind.FieldDeclaration);
        }

        private static bool IsPublicEverywhere(ISymbol symbol)
        {
            for (ISymbol current = symbol; current != null && !(current is INamespaceSymbol); current = current.ContainingSymbol)
            {
                if (current.DeclaredAccessibility != Accessibility.Public) return false;
            }
            return true;
        }

        private static bool IsMutableCollection(ITypeSymbol type)
        {
            foreach (string name in MutableCollections)
            {
                if (SymbolNames.IsNamed(type.OriginalDefinition, name)) return true;
            }
            return false;
        }

        private static bool HoldsReadOnlyWrapper(SyntaxNodeAnalysisContext context, VariableDeclaratorSyntax variable)
        {
            if (variable.Initializer == null) return false;
            ITypeSymbol value = context.SemanticModel.GetTypeInfo(variable.Initializer.Value, context.CancellationToken).Type;
            if (value == null) return false;
            foreach (string name in ReadOnlyWrappers)
            {
                if (SymbolNames.IsOrDerivesFrom(value, name)) return true;
            }
            return false;
        }
    }
}