using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5105: a ThrowIfCancellationRequested() right next to an await that was given the same token,
    // as in "await UniTask.Delay(500, ct); ct.ThrowIfCancellationRequested();".
    //
    // When the token is cancelled, the await throws OperationCanceledException itself, so the check
    // after it never runs cancelled and the check before it only moves the throw up one line. It's
    // one line of noise per await, and Starlets2 had close to a hundred of them.
    //
    // Checked: an expression statement "token.ThrowIfCancellationRequested();" whose previous or
    // next statement in the same block is an await, standing alone, assigned, declared into a local
    // or returned. The await counts when the called method gets the same token as an argument,
    // written the same way ("ct" and "ct", "cts.Token" and "cts.Token"), or when
    // WithCancellation(token) or AttachExternalCancellation(token) sits in the call chain. An await
    // ending in SuppressCancellationThrow() doesn't throw, so it never counts. This is a syntax
    // check: a callee that ignores its token isn't noticed. Reported on the check.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RedundantCancellationCheckAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5105",
            "Cancellation check the await already does",
            "The await on the {1} line already throws when '{0}' is cancelled; remove this check",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionStatementSyntax statement = (ExpressionStatementSyntax)context.Node;
                string token = CheckedToken(statement);
                if (token == null || !(statement.Parent is BlockSyntax block)) return;
                int index = block.Statements.IndexOf(statement);

                // After a cancelled await the check is dead code, so that neighbour is named first.
                if (index > 0 && AwaitReceivesToken(block.Statements[index - 1], token)) Report(context, statement, token, "previous");
                else if (index < block.Statements.Count - 1 && AwaitReceivesToken(block.Statements[index + 1], token)) Report(context, statement, token, "next");
            }), SyntaxKind.ExpressionStatement);
        }

        private static void Report(SyntaxNodeAnalysisContext context, ExpressionStatementSyntax statement, string token, string neighbour)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, statement.Expression.GetLocation(), token, neighbour));
        }

        // "ct" from "ct.ThrowIfCancellationRequested();", or null for any other statement.
        private static string CheckedToken(ExpressionStatementSyntax statement)
        {
            if (!(statement.Expression is InvocationExpressionSyntax invocation) || invocation.ArgumentList.Arguments.Count > 0) return null;
            if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "ThrowIfCancellationRequested") return null;
            return Text(access.Expression);
        }

        private static bool AwaitReceivesToken(StatementSyntax statement, string token)
        {
            ExpressionSyntax awaited = AwaitedExpression(statement);
            return awaited != null && ReceivesToken(awaited, token);
        }

        // The operand of the await in "await X;", "y = await X;", "T y = await X;" or "return await X;".
        private static ExpressionSyntax AwaitedExpression(StatementSyntax statement)
        {
            ExpressionSyntax expression;
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    expression = expressionStatement.Expression is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        ? assignment.Right
                        : expressionStatement.Expression;
                    break;
                case LocalDeclarationStatementSyntax declaration:
                    expression = declaration.Declaration.Variables.Count == 1 ? declaration.Declaration.Variables[0].Initializer?.Value : null;
                    break;
                case ReturnStatementSyntax returnStatement:
                    expression = returnStatement.Expression;
                    break;
                default:
                    return null;
            }
            return (expression as AwaitExpressionSyntax)?.Expression;
        }

        // The innermost call takes the token as an argument, or a WithCancellation or
        // AttachExternalCancellation on the way out does. Walks "Load(id).AsUniTask().WithCancellation(ct)"
        // from the outside in.
        private static bool ReceivesToken(ExpressionSyntax awaited, string token)
        {
            bool received = false;
            ExpressionSyntax current = awaited;
            while (current is InvocationExpressionSyntax invocation)
            {
                string name = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? (invocation.Expression as SimpleNameSyntax)?.Identifier.ValueText;
                if (name == "SuppressCancellationThrow") return false;
                ExpressionSyntax receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
                bool innermost = !(receiver is InvocationExpressionSyntax);
                if ((innermost || name == "WithCancellation" || name == "AttachExternalCancellation") && HasTokenArgument(invocation, token)) received = true;
                current = receiver;
            }
            return received;
        }

        private static bool HasTokenArgument(InvocationExpressionSyntax invocation, string token)
        {
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (Text(argument.Expression) == token) return true;
            }
            return false;
        }

        private static string Text(ExpressionSyntax expression)
        {
            return expression.WithoutTrivia().ToString();
        }
    }
}
