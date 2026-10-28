using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

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
    // method has a known cancellation contract: UniTask's delays, frame waits, WaitUntil, WaitWhile
    // and WaitUntilValueChanged, or its cancellation wrappers. WaitUntilCanceled and unknown methods
    // aren't checked. The token must be a local or by-value parameter, not a replaceable field or
    // property, and no lambda or local function in the member may assign it. For a check before the
    // await only the call that runs first
    // counts: in "OnHideAsync(ct).AttachExternalCancellation(ct)" the project method runs before the
    // wrapper looks at the token, so the check still stops it from starting. An await
    // ending in SuppressCancellationThrow() doesn't throw, so it never counts. Reported on the check.
    // Cancellation can still arrive between statements. Keep the check with a suppression when that
    // gap matters, such as a token cancelled from another thread.
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
                string token = CheckedToken(context, statement);
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
        private static string CheckedToken(SyntaxNodeAnalysisContext context, ExpressionStatementSyntax statement)
        {
            if (!(statement.Expression is InvocationExpressionSyntax invocation) || invocation.ArgumentList.Arguments.Count > 0) return null;
            if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "ThrowIfCancellationRequested") return null;
            if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)
                || !SymbolNames.IsNamed(method.ContainingType, "System.Threading.CancellationToken")) return null;
            ISymbol token = context.SemanticModel.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol;
            if (!(token is ILocalSymbol local && !local.IsRef) && !(token is IParameterSymbol parameter && parameter.RefKind == RefKind.None)) return null;
            SyntaxNode owner = statement.FirstAncestorOrSelf<MemberDeclarationSyntax>();
            if (owner != null && ClosureReplaces(context, owner, token)) return null;
            return Text(access.Expression);
        }

        // A lambda or local function that assigns the token can swap it during the await.
        private static bool ClosureReplaces(SyntaxNodeAnalysisContext context, SyntaxNode owner, ISymbol token)
        {
            foreach (SyntaxNode closure in owner.DescendantNodes().Where(node => node is AnonymousFunctionExpressionSyntax || node is LocalFunctionStatementSyntax))
            {
                foreach (IdentifierNameSyntax name in closure.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (name.Identifier.ValueText != token.Name || !SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, token)) continue;
                    if (name.Parent is AssignmentExpressionSyntax assignment && assignment.Left == name
                        || name.Parent is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None) && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword)
                        || name.Parent is RefExpressionSyntax) return true;
                }
            }
            return false;
        }

        private static bool AwaitReceivesToken(SyntaxNodeAnalysisContext context, StatementSyntax statement, string token, bool onlyFirstCall)
        {
            ExpressionSyntax awaited = AwaitedExpression(statement);
            if (!onlyFirstCall && statement is ExpressionStatementSyntax expression && expression.Expression is AssignmentExpressionSyntax assignment
                && !(context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is ILocalSymbol or IParameterSymbol)) return false;
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
                if (onlyFirstCall && invocation.ArgumentList.DescendantNodes().Any(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or AssignmentExpressionSyntax or AwaitExpressionSyntax
                    || node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PostIncrementExpression)
                    || node.IsKind(SyntaxKind.PreDecrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression))) return false;
                ExpressionSyntax receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
                bool innermost = !(receiver is InvocationExpressionSyntax);
                bool wrapper = !onlyFirstCall && (name == "WithCancellation" || name == "AttachExternalCancellation");
                if ((innermost || wrapper) && HasTokenArgument(invocation, token) && ThrowsOnCancellation(context, invocation)) received = true;
                current = receiver;
            }
            return received;
        }

        private static bool ThrowsOnCancellation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return false;
            method = method.ReducedFrom ?? method;
            if (SymbolNames.IsNamed(method.ContainingType, "Cysharp.Threading.Tasks.UniTask"))
            {
                switch (method.Name)
                {
                    case "Delay":
                    case "DelayFrame":
                    case "Yield":
                    case "NextFrame":
                    case "WaitForEndOfFrame":
                    case "WaitForFixedUpdate":
                    case "WaitUntil":
                    case "WaitWhile":
                    case "WaitUntilValueChanged":
                        return true;
                }
            }
            return method.Name == "AttachExternalCancellation" && SymbolNames.IsNamed(method.ContainingType, "Cysharp.Threading.Tasks.UniTaskExtensions")
                || method.Name == "WithCancellation" && SymbolNames.IsNamed(method.ContainingType, "Cysharp.Threading.Tasks.UnityAsyncExtensions");
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
