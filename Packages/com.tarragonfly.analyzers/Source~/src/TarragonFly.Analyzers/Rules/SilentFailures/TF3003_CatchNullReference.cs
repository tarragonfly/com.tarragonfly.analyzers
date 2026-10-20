using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3003: catching NullReferenceException.
    //
    // It hides the actual bug, a missing reference (often an unassigned Inspector field), instead of
    // checking for null where it can happen. Unity objects make this worse: a destroyed object can
    // throw MissingReferenceException in one place and NullReferenceException in another.
    //
    // Filters and rethrows don't change anything. A filter that is a type test of the caught exception,
    // or of one of its members such as InnerException, counts like catching the type it tests, as in
    // "catch (Exception e) when (e is NullReferenceException)". Reported on the caught or tested type.
    // Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CatchNullReferenceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3003",
            "Catching NullReferenceException",
            "Check for null where it can happen instead of catching NullReferenceException",
            "SilentFailures",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeSyntax type = ((CatchDeclarationSyntax)context.Node).Type;
                if (SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type, "System.NullReferenceException"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, type.GetLocation()));
                }
            }), SyntaxKind.CatchDeclaration);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                CatchFilterClauseSyntax filter = (CatchFilterClauseSyntax)context.Node;
                if (!(filter.Parent is CatchClauseSyntax clause) || clause.Declaration == null || clause.Declaration.Identifier.IsKind(SyntaxKind.None)) return;
                if (!TryGetTypeTest(filter.FilterExpression, out ExpressionSyntax tested, out TypeSyntax type)) return;
                ISymbol caught = context.SemanticModel.GetDeclaredSymbol(clause.Declaration, context.CancellationToken);
                if (caught == null || !SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(Root(tested), context.CancellationToken).Symbol, caught)) return;
                if (SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type, "System.NullReferenceException"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, type.GetLocation()));
                }
            }), SyntaxKind.CatchFilterClause);
        }

        // "e is T" or "e is T name", in parentheses or not.
        private static bool TryGetTypeTest(ExpressionSyntax condition, out ExpressionSyntax tested, out TypeSyntax type)
        {
            while (condition is ParenthesizedExpressionSyntax parenthesized) condition = parenthesized.Expression;
            tested = null;
            type = null;
            switch (condition)
            {
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression):
                    tested = binary.Left;
                    type = binary.Right as TypeSyntax;
                    break;
                case IsPatternExpressionSyntax pattern when pattern.Pattern is DeclarationPatternSyntax declaration:
                    tested = pattern.Expression;
                    type = declaration.Type;
                    break;
                case IsPatternExpressionSyntax pattern when pattern.Pattern is TypePatternSyntax typePattern:
                    tested = pattern.Expression;
                    type = typePattern.Type;
                    break;
            }
            return type != null;
        }

        // "e" in "e" or "e.InnerException".
        private static ExpressionSyntax Root(ExpressionSyntax expression)
        {
            while (expression is MemberAccessExpressionSyntax access) expression = access.Expression;
            return expression;
        }
    }
}