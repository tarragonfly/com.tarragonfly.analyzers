using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3001: an empty block inside a method, such as "if (save == null) { }" or an empty catch, loop,
    // else, using or lock body, and switch statements with no sections.
    //
    // An empty branch is either unfinished work or a case that silently does nothing; a comment saying
    // why nothing happens is enough to keep it.
    //
    // Any comment, preprocessor directive or #if-disabled code between the braces counts as content.
    // Method, constructor, destructor and operator bodies aren't checked (TF8001 covers empty methods),
    // nor are lambda and anonymous method bodies. Property accessors and local functions are, but event
    // add and remove accessors aren't, since an empty pair is how a test stand-in ignores subscribers.
    // An empty catch with a "when" filter or an OperationCanceledException type is skipped, as TF3004
    // skips it. So is the body of a while, do or for loop whose condition or incrementor does the work,
    // as in "while (Step()) { }". Reported on the block, or on the whole switch statement.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EmptyBlockAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3001",
            "Empty block",
            "Fill in this empty block or add a comment saying why nothing happens here",
            "SilentFailures",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BlockSyntax block = (BlockSyntax)context.Node;
                if (block.Statements.Count > 0 || block.Parent is BaseMethodDeclarationSyntax || block.Parent is AnonymousFunctionExpressionSyntax) return;
                if (block.Parent.IsKind(SyntaxKind.AddAccessorDeclaration) || block.Parent.IsKind(SyntaxKind.RemoveAccessorDeclaration)) return;
                if (block.Parent is CatchClauseSyntax catchClause && IgnoredExceptionAnalyzer.IsIgnoredOnPurpose(context.SemanticModel, catchClause, context.CancellationToken)) return;
                if (LoopHeaderDoesTheWork(block.Parent)) return;
                if (Braces.HaveOnlyWhitespaceBetween(block.OpenBraceToken, block.CloseBraceToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, block.GetLocation()));
                }
            }), SyntaxKind.Block);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SwitchStatementSyntax switchStatement = (SwitchStatementSyntax)context.Node;
                if (switchStatement.Sections.Count == 0 && Braces.HaveOnlyWhitespaceBetween(switchStatement.OpenBraceToken, switchStatement.CloseBraceToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, switchStatement.GetLocation()));
                }
            }), SyntaxKind.SwitchStatement);
        }

        // A while, do or for loop whose condition or incrementor calls a method, assigns or increments,
        // so the loop runs that until it's done and needs no body.
        private static bool LoopHeaderDoesTheWork(SyntaxNode loop)
        {
            switch (loop)
            {
                case WhileStatementSyntax whileStatement:
                    return HasSideEffect(whileStatement.Condition);
                case DoStatementSyntax doStatement:
                    return HasSideEffect(doStatement.Condition);
                case ForStatementSyntax forStatement:
                    if (forStatement.Condition != null && HasSideEffect(forStatement.Condition)) return true;
                    foreach (ExpressionSyntax incrementor in forStatement.Incrementors)
                    {
                        if (HasSideEffect(incrementor)) return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool HasSideEffect(ExpressionSyntax expression)
        {
            foreach (SyntaxNode node in expression.DescendantNodesAndSelf())
            {
                if (node is InvocationExpressionSyntax || node is AssignmentExpressionSyntax) return true;
                if (node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PreDecrementExpression)
                    || node.IsKind(SyntaxKind.PostIncrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression)) return true;
            }
            return false;
        }
    }
}