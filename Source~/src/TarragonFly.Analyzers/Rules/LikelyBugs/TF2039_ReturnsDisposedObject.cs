using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2039: returning a variable that its using disposes on the way out, as in "using (FileStream
    // file = File.OpenRead(path)) { return file; }".
    //
    // The using disposes the object as the method returns, so the caller always gets a closed stream
    // or a cancelled token source and fails on first use.
    //
    // Both using statements and using declarations ("using FileStream file = ...;") count, for a return
    // of the variable itself; returning something read from it, such as file.Length, is fine. Lambdas
    // and local functions inside aren't checked. Reported on the using keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReturnsDisposedObjectAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2039",
            "Returning an object the using disposes",
            "'{0}' is disposed by this using as the method returns, so the caller gets a disposed object",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                UsingStatementSyntax statement = (UsingStatementSyntax)context.Node;
                if (statement.Declaration == null) return;
                Report(context, statement.UsingKeyword, statement.Declaration, new[] { statement.Statement });
            }), SyntaxKind.UsingStatement);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                LocalDeclarationStatementSyntax declaration = (LocalDeclarationStatementSyntax)context.Node;
                if (declaration.UsingKeyword.IsKind(SyntaxKind.None) || !(declaration.Parent is BlockSyntax block)) return;
                IEnumerable<StatementSyntax> after = block.Statements.SkipWhile(s => s != declaration).Skip(1);
                Report(context, declaration.UsingKeyword, declaration.Declaration, after);
            }), SyntaxKind.LocalDeclarationStatement);
        }

        private static void Report(SyntaxNodeAnalysisContext context, SyntaxToken usingKeyword, VariableDeclarationSyntax declaration, IEnumerable<StatementSyntax> scope)
        {
            HashSet<string> names = new HashSet<string>(declaration.Variables.Select(v => v.Identifier.ValueText));
            foreach (StatementSyntax statement in scope)
            {
                foreach (SyntaxNode node in statement.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax) && !(child is LocalFunctionStatementSyntax)))
                {
                    if (!(node is ReturnStatementSyntax returned) || !(Unwrap(returned.Expression) is IdentifierNameSyntax name) || !names.Contains(name.Identifier.ValueText)) continue;
                    if (!(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is ILocalSymbol local)) continue;
                    if (!declaration.Variables.Any(v => SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetDeclaredSymbol(v, context.CancellationToken), local))) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, usingKeyword.GetLocation(), local.Name));
                    return;
                }
            }
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
            return expression;
        }
    }
}