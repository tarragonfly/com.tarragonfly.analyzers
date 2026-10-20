using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2501: a constructor of a class that can be inherited calling one of its own overridable
    // methods, as in "Enemy() { SetUpStats(); }" with "protected virtual void SetUpStats()".
    //
    // The subclass override runs before the subclass constructor has set its fields, so it sees
    // default values: a Boss override reading its own config gets null.
    //
    // Virtual, abstract and not-sealed override methods count, called on "this" or with no receiver.
    // Calls inside lambdas and local functions, and property accesses, aren't checked; sealed classes
    // are fine. Reported on the called method, "this." included. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class VirtualCallInConstructorAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2501",
            "Overridable method called in a constructor",
            "'{0}' can be overridden, and an override runs before the subclass constructor has set its fields",
            "Inheritance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                bool onThis = invocation.Expression is SimpleNameSyntax
                    || (invocation.Expression is MemberAccessExpressionSyntax access && access.Expression is ThisExpressionSyntax);
                if (!onThis) return;

                ConstructorDeclarationSyntax constructor = null;
                foreach (SyntaxNode ancestor in invocation.Ancestors())
                {
                    if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax) return;
                    if (ancestor is ConstructorDeclarationSyntax found) { constructor = found; break; }
                    if (ancestor is MemberDeclarationSyntax) return;
                }
                if (constructor == null || constructor.Modifiers.Any(SyntaxKind.StaticKeyword)) return;

                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || method.IsStatic) return;
                if (!method.IsVirtual && !method.IsAbstract && !(method.IsOverride && !method.IsSealed)) return;
                INamedTypeSymbol type = context.SemanticModel.GetDeclaredSymbol(constructor, context.CancellationToken)?.ContainingType;
                if (type == null || type.IsSealed || !IsOwnMember(type, method)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), method.Name));
            }), SyntaxKind.InvocationExpression);
        }

        // A method of the class or a base class, not Invoke on a delegate held in a local or field.
        private static bool IsOwnMember(INamedTypeSymbol type, IMethodSymbol method)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, method.ContainingType.OriginalDefinition) || SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, method.ContainingType.OriginalDefinition)) return true;
            }
            return false;
        }
    }
}