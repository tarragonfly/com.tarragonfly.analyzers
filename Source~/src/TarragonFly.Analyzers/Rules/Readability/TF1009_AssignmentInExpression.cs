using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1009: an assignment used as part of a larger expression, such as "Use(health = 0)" or "if
    // (alive && (dead = hp <= 0))".
    //
    // The side effect hides inside a condition or argument, where readers expect a plain value, and
    // "=" next to "==" is an easy typo to miss.
    //
    // Allowed: an assignment as its own statement, chained assignments (a = b = 0), variable and field
    // initializers, return and yield return values and expression bodies (=> x = 1, return x = 1), for
    // loop initializers and updates, the value a switch statement, lock or using statement takes, ??=
    // anywhere ((list ??= new()).Add(x)), the right side of ?? when it assigns the left side (cache ??
    // (cache = Load())), object initializer members, and the read-and-compare loop idiom: an assignment
    // compared with ==, !=, <, <=, > or >=, or tested with an is pattern, directly in an if, while, do
    // or for condition, as in "while ((line = reader.ReadLine()) != null)". A switch expression on an
    // assignment is reported. Reported on the assignment operator.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AssignmentInExpressionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1009",
            "Assignment inside an expression",
            "Move this assignment out of the expression into its own statement",
            "Readability",
            CodeKind.All);

        private static readonly SyntaxKind[] AssignmentKinds =
        {
            SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.DivideAssignmentExpression, SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression, SyntaxKind.CoalesceAssignmentExpression
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (!IsAllowed(assignment)) context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.OperatorToken.GetLocation()));
            }), AssignmentKinds);
        }

        private static bool IsAllowed(AssignmentExpressionSyntax assignment)
        {
            if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)) return true;
            SyntaxNode node = SkipParenthesesUp(assignment, out SyntaxNode parent);
            switch (parent)
            {
                case ExpressionStatementSyntax _:
                case EqualsValueClauseSyntax _:
                case ArrowExpressionClauseSyntax _:
                case ReturnStatementSyntax _:
                case YieldStatementSyntax _:
                case SwitchStatementSyntax _:
                case LockStatementSyntax _:
                case UsingStatementSyntax _:
                case ForStatementSyntax f when f.Condition != node:
                case InitializerExpressionSyntax i when i.IsKind(SyntaxKind.ObjectInitializerExpression) || i.IsKind(SyntaxKind.WithInitializerExpression):
                    return true;
                case AssignmentExpressionSyntax chain:
                    return chain.Right == node;
                case LambdaExpressionSyntax lambda:
                    return lambda.Body == node;
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    return coalesce.Right == node && SyntaxFactory.AreEquivalent(coalesce.Left, assignment.Left);
                case BinaryExpressionSyntax comparison when IsComparison(comparison):
                    return IsLoopOrIfCondition(comparison);
                case IsPatternExpressionSyntax pattern:
                    return IsLoopOrIfCondition(pattern);
                default:
                    return false;
            }
        }

        private static bool IsComparison(BinaryExpressionSyntax binary)
        {
            switch (binary.Kind())
            {
                case SyntaxKind.EqualsExpression:
                case SyntaxKind.NotEqualsExpression:
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return true;
                default:
                    return false;
            }
        }

        // The comparison or pattern test is an if, while, do or for condition, possibly joined with &&
        // or ||.
        private static bool IsLoopOrIfCondition(ExpressionSyntax comparison)
        {
            SyntaxNode node = comparison;
            SyntaxNode parent = comparison.Parent;
            while (parent is ParenthesizedExpressionSyntax || parent.IsKind(SyntaxKind.LogicalAndExpression) || parent.IsKind(SyntaxKind.LogicalOrExpression))
            {
                node = parent;
                parent = parent.Parent;
            }
            switch (parent)
            {
                case IfStatementSyntax s: return s.Condition == node;
                case WhileStatementSyntax s: return s.Condition == node;
                case DoStatementSyntax s: return s.Condition == node;
                case ForStatementSyntax s: return s.Condition == node;
                default: return false;
            }
        }

        private static SyntaxNode SkipParenthesesUp(SyntaxNode node, out SyntaxNode parent)
        {
            parent = node.Parent;
            while (parent is ParenthesizedExpressionSyntax)
            {
                node = parent;
                parent = parent.Parent;
            }
            return node;
        }
    }
}