using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2045: a set, init, add or remove accessor that never uses "value".
    //
    // "set { health = 5; }" ignores what the caller assigns, so "player.Health = 100" silently does
    // something else. If the property shouldn't be settable, drop the setter or throw.
    //
    // A use inside a lambda or nameof(value) counts. Accessors whose whole body throws are fine, and
    // explicit interface implementations aren't checked. An empty "set { }" is reported, overrides
    // included. Reported on the accessor keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SetterIgnoresValueAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2045",
            "Setter ignores value",
            "This {0} accessor never uses 'value'",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AccessorDeclarationSyntax accessor = (AccessorDeclarationSyntax)context.Node;
                SyntaxNode body = (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
                if (body == null || OnlyThrows(accessor) || IsExplicitInterfaceImplementation(accessor)) return;

                foreach (IdentifierNameSyntax name in body.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (name.Identifier.ValueText != "value") continue;
                    if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IParameterSymbol parameter && parameter.IsImplicitlyDeclared) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, accessor.Keyword.GetLocation(), accessor.Keyword.Text));
            }), SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration, SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration);
        }

        private static bool OnlyThrows(AccessorDeclarationSyntax accessor)
        {
            if (accessor.ExpressionBody != null) return accessor.ExpressionBody.Expression is ThrowExpressionSyntax;
            return accessor.Body.Statements.Count == 1 && accessor.Body.Statements[0] is ThrowStatementSyntax;
        }

        private static bool IsExplicitInterfaceImplementation(AccessorDeclarationSyntax accessor)
        {
            switch (accessor.Parent?.Parent)
            {
                case BasePropertyDeclarationSyntax property: return property.ExplicitInterfaceSpecifier != null;
                default: return false;
            }
        }
    }
}