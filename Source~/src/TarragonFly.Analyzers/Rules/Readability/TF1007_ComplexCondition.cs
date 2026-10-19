using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1007: an expression with more &&, || and ?: operators than max_condition_operators (default 3).
    //
    // A condition like "if (alive && !stunned && (grounded || coyoteTime > 0) && !paused)" is hard to
    // check by eye. Name its parts with locals or a method such as CanJump().
    //
    // Counting runs through other operators, parentheses and !, so "(a ? 1 : 0) + (b ? 1 : 0)" adds up.
    // It stops at anything else: method arguments, lambdas, casts, assignments, initializers and
    // interpolations each start a separate count. ??, &, |, ^ and is-pattern and/or don't count. #if
    // and #elif conditions are checked too. Equals overrides and IEquatable<T>.Equals methods are
    // skipped, since comparing every field is what they're for. Reported once, on the whole expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ComplexConditionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1007",
            "Condition is too complex",
            "This expression has {0} &&, || and ?: operators, more than the limit of {1}; name its parts with locals or a method",
            "Readability",
            CodeKind.All,
            description: "The limit is max_condition_operators in the settings file.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxConditionOperators;
            start.RegisterSemanticModelAction(context =>
            {
                SyntaxTree tree = context.SemanticModel.SyntaxTree;
                if (!scope.IsAnalyzed(tree)) return;
                SyntaxNode root = tree.GetRoot(context.CancellationToken);
                foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
                {
                    if (!(node is ExpressionSyntax) || !CountsThrough(node) || CountsThrough(node.Parent)) continue;
                    int operators = CountOperators(node);
                    if (operators <= limit || IsInEqualsMethod(context.SemanticModel, node)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), operators, limit));
                }
            });
        }

        // An override of object.Equals, or the Equals that implements IEquatable<T>.
        private static bool IsInEqualsMethod(SemanticModel model, SyntaxNode node)
        {
            MethodDeclarationSyntax declaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (declaration == null || declaration.Identifier.ValueText != "Equals") return false;
            if (!(model.GetDeclaredSymbol(declaration) is IMethodSymbol method) || method.Parameters.Length != 1) return false;
            if (method.IsOverride) return true;

            foreach (INamedTypeSymbol candidate in method.ContainingType.AllInterfaces)
            {
                if (!SymbolNames.IsNamed(candidate.OriginalDefinition, "System.IEquatable`1")) continue;
                foreach (ISymbol member in candidate.GetMembers("Equals"))
                {
                    if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method)) return true;
                }
            }
            return false;
        }

        private static int CountOperators(SyntaxNode expression)
        {
            int count = 0;
            Stack<SyntaxNode> pending = new Stack<SyntaxNode>();
            pending.Push(expression);
            while (pending.Count > 0)
            {
                SyntaxNode node = pending.Pop();
                if (IsCounted(node)) count++;
                foreach (SyntaxNode child in node.ChildNodes())
                {
                    if (CountsThrough(child)) pending.Push(child);
                }
            }
            return count;
        }

        private static bool IsCounted(SyntaxNode node)
        {
            return node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression) || node.IsKind(SyntaxKind.ConditionalExpression);
        }

        // Expressions a count continues through; any other expression starts its own count.
        private static bool CountsThrough(SyntaxNode node)
        {
            return node is BinaryExpressionSyntax
                || node is ConditionalExpressionSyntax
                || node is ParenthesizedExpressionSyntax
                || node is PrefixUnaryExpressionSyntax
                || node is PostfixUnaryExpressionSyntax;
        }
    }
}