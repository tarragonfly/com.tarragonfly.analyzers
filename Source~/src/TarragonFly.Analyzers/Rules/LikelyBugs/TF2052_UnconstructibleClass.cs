using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2052: a class whose constructors are all private and that never creates itself.
    //
    // Nothing can ever create it, so its instance members are dead code, or a factory method is
    // missing.
    //
    // A class creates itself with "new ItsName(...)" or "new()" anywhere inside it, nested types
    // included. A class with no instance members but static members or nested types is a holder for
    // those and fine. Static classes and records aren't checked; abstract ones are, since private
    // constructors also stop anything from deriving. Reported on the class name.
    //
    // Unity objects aren't checked, since Unity creates MonoBehaviours and ScriptableObjects itself,
    // private constructor or not.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnconstructibleClassAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2052",
            "Class nothing can construct",
            "'{0}' has only private constructors and nothing inside that uses them, so it can never be created",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (type.TypeKind != TypeKind.Class || type.IsStatic || type.IsRecord || Frameworks.Unity.IsUnityObject(type)) return;

                foreach (IMethodSymbol constructor in type.InstanceConstructors)
                {
                    if (constructor.IsImplicitlyDeclared || constructor.DeclaredAccessibility != Accessibility.Private) return;
                }
                bool instanceMembers = type.GetMembers().Any(member => !member.IsStatic && !member.IsImplicitlyDeclared
                    && !(member is IMethodSymbol method && method.MethodKind == MethodKind.Constructor) && !(member is INamedTypeSymbol));
                bool holder = !instanceMembers && type.GetMembers().Any(member => (member.IsStatic && !member.IsImplicitlyDeclared) || member is INamedTypeSymbol);
                if (holder || CreatesItself(type, context.CancellationToken)) return;

                Location location = type.Locations[0];
                if (location.IsInSource && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
            }, SymbolKind.NamedType);
        }

        // "new ItsName(...)" or a target-typed "new()" anywhere in the class, nested types included.
        private static bool CreatesItself(INamedTypeSymbol type, System.Threading.CancellationToken ct)
        {
            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                foreach (SyntaxNode node in reference.GetSyntax(ct).DescendantNodes())
                {
                    if (node is ImplicitObjectCreationExpressionSyntax) return true;
                    if (!(node is ObjectCreationExpressionSyntax creation)) continue;
                    TypeSyntax created = creation.Type is QualifiedNameSyntax qualified ? qualified.Right : creation.Type;
                    if (created is SimpleNameSyntax name && name.Identifier.ValueText == type.Name) return true;
                }
            }
            return false;
        }
    }
}