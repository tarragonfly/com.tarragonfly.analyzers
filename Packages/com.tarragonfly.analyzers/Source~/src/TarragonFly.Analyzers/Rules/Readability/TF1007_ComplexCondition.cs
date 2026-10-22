using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1007: an expression with more &&, || and ?: operators than max_condition_operators (default 4).
    //
    // A condition like "if (alive && !stunned && (grounded || coyoteTime > 0) && !paused)" is hard to
    // check by eye. Name its parts with locals or a method such as CanJump().
    //
    // Counting runs through other operators, parentheses and !, so "(a ? 1 : 0) + (b ? 1 : 0)" adds up.
    // It stops at anything else: method arguments, lambdas, casts, assignments, initializers and
    // interpolations each start a separate count. ??, &, |, ^ and is-pattern and/or don't count. #if
    // and #elif conditions are checked too. Equals overrides and IEquatable<T>.Equals methods are
    // skipped, since comparing every field is what they're for. So is a plain chain of only && or only
    // || that is the whole body of a method, property or local function, such as
    // "bool HasReferences() => _root != null && _target != null && ...": the method already names it.
    // Reported once, on the whole expression.
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
                    int operators = CountOperators(node, out bool oneKind);
                    if (operators <= limit || IsInEqualsMethod(context.SemanticModel, node)) continue;
                    if (oneKind && IsWholeMemberBody(node)) continue;
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

        // The expression is all a method, property, getter or local function returns, through "=>" or
        // a lone return statement. Lambdas don't count, since they have no name.
        private static bool IsWholeMemberBody(SyntaxNode expression)
        {
            SyntaxNode body = expression.Parent;
            if (body is ReturnStatementSyntax returnStatement)
            {
                if (!(returnStatement.Parent is BlockSyntax block) || block.Statements.Count != 1) return false;
                body = block;
            }
            else if (!(body is ArrowExpressionClauseSyntax))
            {
                return false;
            }
            SyntaxNode member = body.Parent;
            return member is MethodDeclarationSyntax || member is PropertyDeclarationSyntax || member is LocalFunctionStatementSyntax
                || member.IsKind(SyntaxKind.GetAccessorDeclaration);
        }

        // The number of &&, || and ?: operators an expression counts toward the limit. TF1006 uses it
        // too, to leave alone an if whose merged condition would go over the limit.
        internal static int CountOperators(SyntaxNode expression)
        {
            return CountOperators(expression, out _);
        }

        // oneKind is true when every counted operator is && or every one is ||, with no ?:.
        private static int CountOperators(SyntaxNode expression, out bool oneKind)
        {
            int count = 0;
            SyntaxKind firstKind = SyntaxKind.None;
            oneKind = true;
            Stack<SyntaxNode> pending = new Stack<SyntaxNode>();
            pending.Push(expression);
            while (pending.Count > 0)
            {
                SyntaxNode node = pending.Pop();
                if (IsCounted(node))
                {
                    count++;
                    if (firstKind == SyntaxKind.None) firstKind = node.Kind();
                    if (!node.IsKind(firstKind) || node.IsKind(SyntaxKind.ConditionalExpression)) oneKind = false;
                }
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