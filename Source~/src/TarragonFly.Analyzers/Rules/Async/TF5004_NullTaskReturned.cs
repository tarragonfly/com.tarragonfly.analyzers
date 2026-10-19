using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5004: a method or property returning Task that hands back null, as in "public Task SaveAsync()
    // => null;".
    //
    // Awaiting null throws NullReferenceException. Return Task.CompletedTask or Task.FromResult(...)
    // instead, or make the method async.
    //
    // Only non-async members returning Task or Task<T> count, with a literal null as the whole return
    // value; a null in "?:", default, and lambdas aren't checked. Reported on the null.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NullTaskReturnedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5004",
            "Null returned instead of a Task",
            "Awaiting this null throws; return Task.CompletedTask or Task.FromResult(...) instead",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax returned = context.Node is ReturnStatementSyntax statement ? statement.Expression : ((ArrowExpressionClauseSyntax)context.Node).Expression;
                if (returned == null || !returned.IsKind(SyntaxKind.NullLiteralExpression)) return;
                if (!(context.SemanticModel.GetEnclosingSymbol(returned.SpanStart, context.CancellationToken) is IMethodSymbol member) || member.IsAsync) return;
                if (member.MethodKind == MethodKind.AnonymousFunction || member.MethodKind == MethodKind.LocalFunction) return;
                ITypeSymbol type = member.ReturnType.OriginalDefinition;
                if (SymbolNames.IsNamed(type, "System.Threading.Tasks.Task") || SymbolNames.IsNamed(type, "System.Threading.Tasks.Task`1"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, returned.GetLocation()));
                }
            }), SyntaxKind.ReturnStatement, SyntaxKind.ArrowExpressionClause);
        }
    }
}