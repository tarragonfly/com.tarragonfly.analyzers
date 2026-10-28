using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1011: an override method or property that only passes through to the base member with the same
    // arguments, which changes nothing.
    //
    // It adds a place to look while debugging and hides that the base behaviour is used unchanged.
    //
    // Kept when it does change something: attributes on the override, sealed overrides, optional
    // parameters (defaults may differ), different or reordered arguments, and Equals and GetHashCode,
    // which are often overridden in pairs on purpose. Property overrides count when every accessor only
    // passes through. Reported on the whole declaration.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OverrideOnlyCallsBaseAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1011",
            "Override only calls the base member",
            "This override only calls the base member; remove it",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                if (!IsPlainOverride(method) || method.Identifier.ValueText == "Equals" || method.Identifier.ValueText == "GetHashCode") return;
                foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
                {
                    if (parameter.Default != null) return;
                }

                ExpressionSyntax call = OnlyExpression(method.Body, method.ExpressionBody);
                if (call is InvocationExpressionSyntax invocation && CallsBaseWithOwnParameters(invocation, method)
                    && context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol owner
                    && owner.OverriddenMethod is IMethodSymbol overridden)
                {
                    if (overridden.Arity > 0) overridden = overridden.Construct(owner.TypeParameters.Cast<ITypeSymbol>().ToArray());
                    if (SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol, overridden))
                        context.ReportDiagnostic(Diagnostic.Create(Rule, method.GetLocation()));
                }
            }), SyntaxKind.MethodDeclaration);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)context.Node;
                if (!IsPlainOverride(property)) return;
                string name = property.Identifier.ValueText;
                if (property.ExpressionBody != null)
                {
                    if (IsBaseAccess(property.ExpressionBody.Expression, name)) context.ReportDiagnostic(Diagnostic.Create(Rule, property.GetLocation()));
                    return;
                }
                if (property.AccessorList == null) return;
                foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
                {
                    if (accessor.AttributeLists.Count > 0 || !OnlyPassesThrough(accessor, name)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, property.GetLocation()));
            }), SyntaxKind.PropertyDeclaration);
        }

        private static bool IsPlainOverride(MemberDeclarationSyntax member)
        {
            if (member.AttributeLists.Count > 0) return false;
            bool isOverride = false;
            foreach (SyntaxToken modifier in member.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.SealedKeyword)) return false;
                if (modifier.IsKind(SyntaxKind.OverrideKeyword)) isOverride = true;
            }
            return isOverride;
        }

        // The single expression of "{ expr; }", "{ return expr; }" or "=> expr".
        private static ExpressionSyntax OnlyExpression(BlockSyntax body, ArrowExpressionClauseSyntax expressionBody)
        {
            if (expressionBody != null) return expressionBody.Expression;
            if (body == null || body.Statements.Count != 1) return null;
            switch (body.Statements[0])
            {
                case ExpressionStatementSyntax statement: return statement.Expression;
                case ReturnStatementSyntax statement: return statement.Expression;
                default: return null;
            }
        }

        private static bool CallsBaseWithOwnParameters(InvocationExpressionSyntax invocation, MethodDeclarationSyntax method)
        {
            if (!IsBaseAccess(invocation.Expression, method.Identifier.ValueText)) return false;
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            SeparatedSyntaxList<ParameterSyntax> parameters = method.ParameterList.Parameters;
            if (arguments.Count != parameters.Count) return false;
            for (int i = 0; i < arguments.Count; i++)
            {
                if (arguments[i].NameColon != null || !(arguments[i].Expression is IdentifierNameSyntax name)) return false;
                if (name.Identifier.ValueText != parameters[i].Identifier.ValueText) return false;
            }
            return true;
        }

        private static bool OnlyPassesThrough(AccessorDeclarationSyntax accessor, string property)
        {
            ExpressionSyntax expression = OnlyExpression(accessor.Body, accessor.ExpressionBody);
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) return IsBaseAccess(expression, property);
            return expression is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && IsBaseAccess(assignment.Left, property)
                && assignment.Right is IdentifierNameSyntax value
                && value.Identifier.ValueText == "value";
        }

        private static bool IsBaseAccess(ExpressionSyntax expression, string memberName)
        {
            return expression is MemberAccessExpressionSyntax access
                && access.Expression is BaseExpressionSyntax
                && access.Name.Identifier.ValueText == memberName;
        }
    }
}
