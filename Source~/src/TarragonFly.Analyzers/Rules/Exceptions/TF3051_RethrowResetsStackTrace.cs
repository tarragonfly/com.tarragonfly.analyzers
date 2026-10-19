using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3051: "throw e;" of the exception its own catch clause caught.
    //
    // That restarts the stack trace at the catch, so the Unity Console no longer shows where the
    // problem happened. "throw;" rethrows with the original trace.
    //
    // Only the innermost catch's own variable counts, even after it was reassigned. A catch inside a
    // lambda or local function starts over. Reported on the throw statement. Game code only.
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
                        context.ReportDiagnostic(Diagnostic.Create(Rule, throwStatement.GetLocation(), thrown.Identifier.ValueText));
                    }
                    return;
                }
            }), SyntaxKind.ThrowStatement);
        }
    }
}