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
    // WithCancellation(token) or AttachExternalCancellation(token) sits in the call chain, and that
    // method is UniTask's or Unity's. Those throw as soon as they get a cancelled token. A project
    // method might return early without looking at the token, so neither the check before its await
    // nor the one after it is dead code. For a check before the await only the call that runs first
    // counts: in "OnHideAsync(ct).AttachExternalCancellation(ct)" the project method runs before the
    // wrapper looks at the token, so the check still stops it from starting. An await
    // ending in SuppressCancellationThrow() doesn't throw, so it never counts. Reported on the check.
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
                if (index > 0 && AwaitReceivesToken(context, block.Statements[index - 1], token, false)) Report(context, statement, token, "previous");
                else if (index < block.Statements.Count - 1 && AwaitReceivesToken(context, block.Statements[index + 1], token, true)) Report(context, statement, token, "next");
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

        private static bool AwaitReceivesToken(SyntaxNodeAnalysisContext context, StatementSyntax statement, string token, bool onlyFirstCall)
        {
            ExpressionSyntax awaited = AwaitedExpression(statement);
            return awaited != null && ReceivesToken(context, awaited, token, onlyFirstCall);
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
        // from the outside in. With onlyFirstCall, the wrappers on the way out don't count, since the
        // innermost call has already run by the time they see the token.
        private static bool ReceivesToken(SyntaxNodeAnalysisContext context, ExpressionSyntax awaited, string token, bool onlyFirstCall)
        {
            bool received = false;
            ExpressionSyntax current = awaited;
            while (current is InvocationExpressionSyntax invocation)
            {
                string name = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText ?? (invocation.Expression as SimpleNameSyntax)?.Identifier.ValueText;
                if (name == "SuppressCancellationThrow") return false;
                ExpressionSyntax receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
                bool innermost = !(receiver is InvocationExpressionSyntax);
                bool wrapper = !onlyFirstCall && (name == "WithCancellation" || name == "AttachExternalCancellation");
                if ((innermost || wrapper) && HasTokenArgument(invocation, token) && IsUniTaskOrUnityCall(context, invocation)) received = true;
                current = receiver;
            }
            return received;
        }

        private static bool IsUniTaskOrUnityCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return false;
            INamedTypeSymbol declaring = method.ContainingType;
            while (declaring.ContainingType != null) declaring = declaring.ContainingType;
            string namespaceName = declaring.ContainingNamespace.ToDisplayString();
            return IsInOrUnder(namespaceName, "Cysharp.Threading.Tasks") || IsInOrUnder(namespaceName, "UnityEngine");
        }

        private static bool IsInOrUnder(string namespaceName, string root)
        {
            return namespaceName == root || namespaceName.StartsWith(root + ".", System.StringComparison.Ordinal);
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
