using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1503: a type parameter that nothing in its class, struct, interface or method uses.
    //
    // Callers still have to supply it, as in Spawn<Enemy>(), so it looks like it changes something.
    //
    // A use in a "where" constraint doesn't count. Methods whose signature is fixed elsewhere aren't
    // checked: overrides, interface implementations, virtual, abstract and extern methods, and
    // methods declared in interfaces. Delegates aren't checked. A partial type split over several files
    // isn't checked either, since each file is analyzed on its own. Reported on the type parameter.
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
                    default:
                        return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, parameter.Identifier.GetLocation(), parameter.Identifier.ValueText));
            }), SyntaxKind.TypeParameter);
        }

        private static bool HasFixedSignature(IMethodSymbol method)
        {
            return method.IsVirtual || method.IsAbstract || method.IsExtern || method.ContainingType?.TypeKind == TypeKind.Interface
                || BaseMembers.IsOverrideOrImplementation(method);
        }

        private static bool IsUsedIn(SyntaxNodeAnalysisContext context, SyntaxNode declaration, ITypeParameterSymbol parameter)
        {
            foreach (SyntaxNode node in declaration.DescendantNodes(n => !(n is TypeParameterConstraintClauseSyntax) && !(n is TypeParameterListSyntax)))
            {
                if (!(node is IdentifierNameSyntax name) || name.Identifier.ValueText != parameter.Name) continue;
                if (SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, parameter)) return true;
            }
            return false;
        }
    }
}