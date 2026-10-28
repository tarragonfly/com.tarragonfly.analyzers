using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3051: "throw e;" of the exception its own catch clause caught.
    //
    // That restarts the stack trace at the catch, so the Unity Console no longer shows where the
    // problem happened. "throw;" rethrows with the original trace.
    //
    // Only the innermost catch's own variable counts, unless an earlier write may have replaced it.
    // Uncalled lambdas and local functions do not replace it. A catch inside either starts over.
    // Reported on the throw statement. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RethrowResetsStackTraceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3051",
            "Rethrow resets the stack trace",
            "'throw {0};' resets the stack trace; write 'throw;' to keep it",
            "Exceptions",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ThrowStatementSyntax throwStatement = (ThrowStatementSyntax)context.Node;
                if (!(throwStatement.Expression is IdentifierNameSyntax thrown)) return;

                for (SyntaxNode ancestor = throwStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax || ancestor is MemberDeclarationSyntax) return;
                    if (!(ancestor is CatchClauseSyntax catchClause)) continue;

                    CatchDeclarationSyntax declaration = catchClause.Declaration;
                    if (declaration == null || declaration.Identifier.IsKind(SyntaxKind.None)) return;
                    ISymbol caught = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
                    ISymbol symbol = context.SemanticModel.GetSymbolInfo(thrown, context.CancellationToken).Symbol;
                    if (caught != null && SymbolEqualityComparer.Default.Equals(caught, symbol))
                    {
                        if (MayReassignBefore(context.SemanticModel, catchClause, caught, throwStatement, new HashSet<SyntaxNode>(), context.CancellationToken)) return;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, throwStatement.GetLocation(), thrown.Identifier.ValueText));
                    }
                    return;
                }
            }), SyntaxKind.ThrowStatement);
        }

        // The throw is null inside a called lambda or local function, where every write counts.
        private static bool MayReassignBefore(SemanticModel model, SyntaxNode body, ISymbol caught, SyntaxNode throwStatement, HashSet<SyntaxNode> visited, CancellationToken ct)
        {
            if (body == null || !visited.Add(body)) return false;
            foreach (SyntaxNode node in body.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax) && !(child is LocalFunctionStatementSyntax)))
            {
                if (throwStatement != null && !MayRunBefore(node, throwStatement, body)) continue;
                if (node is IdentifierNameSyntax name && name.Identifier.ValueText == caught.Name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, ct).Symbol, caught))
                {
                    if (VariableWrites.IsWrite(model.GetOperation(name, ct))) return true;
                }
                if (!(node is InvocationExpressionSyntax invocation) || !(model.GetOperation(invocation, ct) is IInvocationOperation call)) continue;
                if (MayReassignBefore(model, GetLocalFunctionBody(call.TargetMethod, model, ct), caught, null, visited, ct)) return true;
                IEnumerable<IOperation> callbacks = call.Arguments.Select(argument => argument.Value);
                if (call.TargetMethod.MethodKind == MethodKind.DelegateInvoke) callbacks = callbacks.Append(LocalAssignments.InvokedDelegate(call));
                foreach (IOperation callback in callbacks.SelectMany(value => LocalAssignments.PossibleDelegates(model, value, ct)))
                {
                    SyntaxNode callbackBody = callback is IAnonymousFunctionOperation lambda ? lambda.Body.Syntax
                        : GetLocalFunctionBody(((IMethodReferenceOperation)callback).Method, model, ct);
                    if (MayReassignBefore(model, callbackBody, caught, null, visited, ct)) return true;
                }
            }
            return false;
        }

        // A write earlier in the catch counts unless its block returns or throws first. A later one
        // counts when a loop inside the catch can bring it around to the throw.
        private static bool MayRunBefore(SyntaxNode node, SyntaxNode throwStatement, SyntaxNode catchClause)
        {
            if (node.SpanStart >= throwStatement.SpanStart)
            {
                return node.Ancestors().TakeWhile(ancestor => ancestor != catchClause).Any(ancestor => ancestor.Span.Contains(throwStatement.Span)
                    && (ancestor is ForStatementSyntax || ancestor is ForEachStatementSyntax || ancestor is WhileStatementSyntax || ancestor is DoStatementSyntax));
            }
            for (SyntaxNode ancestor = node.Parent; ancestor != null && !ancestor.Span.Contains(throwStatement.Span); ancestor = ancestor.Parent)
            {
                if (ancestor is BlockSyntax block && (block.Statements.LastOrDefault() is ReturnStatementSyntax || block.Statements.LastOrDefault() is ThrowStatementSyntax)) return false;
            }
            return true;
        }

        private static SyntaxNode GetLocalFunctionBody(IMethodSymbol method, SemanticModel model, CancellationToken ct)
        {
            if (method.MethodKind != MethodKind.LocalFunction) return null;
            SyntaxReference reference = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference?.SyntaxTree != model.SyntaxTree || !(reference.GetSyntax(ct) is LocalFunctionStatementSyntax function)) return null;
            return (SyntaxNode)function.Body ?? function.ExpressionBody;
        }
    }
}
