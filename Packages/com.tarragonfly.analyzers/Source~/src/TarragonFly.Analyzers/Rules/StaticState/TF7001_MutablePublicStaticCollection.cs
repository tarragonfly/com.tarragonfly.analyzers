using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7001: a public static collection anyone can change: an array or a mutable collection such as
    // List, Dictionary, HashSet, Queue, Stack, ArrayList, Hashtable, IList, ICollection, ISet or
    // IDictionary. A field that isn't readonly always counts. A readonly one counts when a call, a
    // "new" or the static constructor fills it, as in "= Enum.GetNames(...)", "= new Queue<Enemy>()"
    // or "= list.ToArray()".
    //
    // "readonly" only stops the field being replaced, not its contents being edited, and with domain
    // reload off whatever was added stays there for the next Play Mode session.
    //
    // Fine: read-only types (IReadOnlyList, ReadOnlyCollection, ImmutableArray...), an interface field
    // holding a read-only wrapper such as Array.AsReadOnly(...), and a readonly field holding an array
    // written out in place, which reads as a table of constants: "= { 0.75f, 1f }", "= new string[3]"
    // or "IList<int> Items = new[] { 1 }". So is a readonly field that holds null, nothing, a cast or
    // another field's or property's value; a static constructor counts unless it assigns null. Only
    // public fields count, in types code outside the assembly can reach, so a nested
    // "protected class Styles" counts but an internal or private type doesn't. Reported once per
    // declaration, on the field type. Game code only.
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
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (!(context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol field)) continue;
                    if (!field.IsStatic || field.IsConst || !IsVisibleOutsideAssembly(field)) return;
                    if (!IsMutable(context, field, variable)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Declaration.Type.GetLocation(), field.Name));
                    return;
                }
            }), SyntaxKind.FieldDeclaration);
        }

        private static bool IsMutable(SyntaxNodeAnalysisContext context, IFieldSymbol field, VariableDeclaratorSyntax variable)
        {
            ITypeSymbol type = field.Type;
            if (!(type is IArrayTypeSymbol) && !IsMutableCollection(type)) return false;
            if (field.IsReadOnly && !IsFilledByCode(field, variable, context.CancellationToken)) return false;
            return !HoldsReadOnlyWrapper(context, variable);
        }

        // A public field in types that code outside the assembly can reach: public, protected or
        // protected internal ones, as a nested "protected class Styles" is to subclasses.
        private static bool IsVisibleOutsideAssembly(IFieldSymbol field)
        {
            if (field.DeclaredAccessibility != Accessibility.Public) return false;
            for (INamedTypeSymbol type = field.ContainingType; type != null; type = type.ContainingType)
            {
                Accessibility access = type.DeclaredAccessibility;
                if (access != Accessibility.Public && access != Accessibility.Protected && access != Accessibility.ProtectedOrInternal) return false;
            }
            return true;
        }

        // A readonly field filled from a call or a "new", as in "= Enum.GetNames(typeof(Mode))" or
        // "= new List<Enemy>()", or by the static constructor with anything but null. Matched by name
        // there, since the constructor may sit in another part of a partial class.
        private static bool IsFilledByCode(IFieldSymbol field, VariableDeclaratorSyntax variable, CancellationToken cancellationToken)
        {
            ExpressionSyntax value = variable.Initializer?.Value;
            if (value is InvocationExpressionSyntax || value is BaseObjectCreationExpressionSyntax) return true;
            foreach (IMethodSymbol constructor in field.ContainingType.StaticConstructors)
            {
                foreach (SyntaxReference reference in constructor.DeclaringSyntaxReferences)
                {
                    IEnumerable<AssignmentExpressionSyntax> assignments = reference.GetSyntax(cancellationToken).DescendantNodes().OfType<AssignmentExpressionSyntax>();
                    if (assignments.Any(assignment => AssignedName(assignment.Left) == field.Name && !assignment.Right.IsKind(SyntaxKind.NullLiteralExpression))) return true;
                }
            }
            return false;
        }

        // "Names" in "Names = ..." or "Options.Names = ...".
        private static string AssignedName(ExpressionSyntax target)
        {
            switch (target)
            {
                case IdentifierNameSyntax name: return name.Identifier.ValueText;
                case MemberAccessExpressionSyntax access: return access.Name.Identifier.ValueText;
                default: return null;
            }
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