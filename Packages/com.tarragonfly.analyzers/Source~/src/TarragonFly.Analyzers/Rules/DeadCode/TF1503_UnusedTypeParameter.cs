using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1503: a type parameter that nothing in its class, struct, interface, method or local function
    // uses.
    //
    // Callers still have to supply it, as in Spawn<Enemy>(), so it looks like it changes something.
    //
    // A use in another type parameter's "where" constraint counts, as TItem in "where TList :
    // IList<TItem>", since the parameter can't go without it; a use in its own constraint, as in
    // "where T : IComparable<T>", doesn't. Methods whose signature is fixed elsewhere aren't checked:
    // overrides, interface implementations, virtual, abstract and extern methods, and methods declared
    // in interfaces. Delegates aren't checked. A partial type split over several files isn't checked
    // either, since each file is analyzed on its own. Reported on the type parameter, from its in or
    // out keyword when it has one.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnusedTypeParameterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1503",
            "Unused type parameter",
            "Remove the unused type parameter '{0}'",
            "DeadCode",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeParameterSyntax parameter = (TypeParameterSyntax)context.Node;
                SyntaxNode owner = parameter.Parent?.Parent;
                if (!(context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is ITypeParameterSymbol symbol)) return;

                switch (owner)
                {
                    case TypeDeclarationSyntax _:
                        foreach (SyntaxReference part in symbol.ContainingType.DeclaringSyntaxReferences)
                        {
                            // A part in another file can't be bound from here; count the parameter as used.
                            if (part.SyntaxTree != parameter.SyntaxTree) return;
                            if (IsUsedIn(context, part.GetSyntax(context.CancellationToken), symbol)) return;
                        }
                        break;
                    case MethodDeclarationSyntax method:
                        if (!(context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol methodSymbol) || HasFixedSignature(methodSymbol)) return;
                        if (IsUsedIn(context, method, symbol)) return;
                        break;
                    case LocalFunctionStatementSyntax local:
                        if (IsUsedIn(context, local, symbol)) return;
                        break;
                    default:
                        return;
                }
                Location location = parameter.VarianceKeyword.IsKind(SyntaxKind.None) ? parameter.Identifier.GetLocation()
                    : Location.Create(parameter.SyntaxTree, TextSpan.FromBounds(parameter.VarianceKeyword.SpanStart, parameter.Identifier.Span.End));
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, parameter.Identifier.ValueText));
            }), SyntaxKind.TypeParameter);
        }

        private static bool HasFixedSignature(IMethodSymbol method)
        {
            return method.IsVirtual || method.IsAbstract || method.IsExtern || method.ContainingType?.TypeKind == TypeKind.Interface
                || BaseMembers.IsOverrideOrImplementation(method);
        }

        private static bool IsUsedIn(SyntaxNodeAnalysisContext context, SyntaxNode declaration, ITypeParameterSymbol parameter)
        {
            foreach (SyntaxNode node in declaration.DescendantNodes(n => !(n is TypeParameterListSyntax) && !IsOwnConstraint(context, n, parameter)))
            {
                if (!(node is IdentifierNameSyntax name) || name.Identifier.ValueText != parameter.Name) continue;
                if (SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, parameter)) return true;
            }
            return false;
        }

        private static bool IsOwnConstraint(SyntaxNodeAnalysisContext context, SyntaxNode node, ITypeParameterSymbol parameter)
        {
            return node is TypeParameterConstraintClauseSyntax clause && clause.Name.Identifier.ValueText == parameter.Name
                && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(clause.Name, context.CancellationToken).Symbol, parameter);
        }
    }
}