using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
    // Using statements that declare the variable or assign one, as in
    // "using (file = File.OpenRead(path))", and using declarations ("using FileStream file = ...;")
    // count, for a return of the variable itself, in parentheses or not; returning something read
    // from it, such as file.Length, is fine. An assigned local, parameter or field counts when it's
    // named on its own, not as "this.file". Lambdas and local functions inside aren't checked.
    // Reported on the using keyword.
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
                List<ISymbol> disposed = statement.Declaration != null
                    ? Declared(context.SemanticModel, statement.Declaration, context.CancellationToken)
                    : Assigned(context.SemanticModel, statement.Expression, context.CancellationToken);
                Report(context, statement.UsingKeyword, disposed, new[] { statement.Statement });
            }), SyntaxKind.UsingStatement);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                LocalDeclarationStatementSyntax declaration = (LocalDeclarationStatementSyntax)context.Node;
                if (declaration.UsingKeyword.IsKind(SyntaxKind.None) || !(declaration.Parent is BlockSyntax block)) return;
                IEnumerable<StatementSyntax> after = block.Statements.SkipWhile(s => s != declaration).Skip(1);
                Report(context, declaration.UsingKeyword, Declared(context.SemanticModel, declaration.Declaration, context.CancellationToken), after);
            }), SyntaxKind.LocalDeclarationStatement);
        }

        private static List<ISymbol> Declared(SemanticModel model, VariableDeclarationSyntax declaration, CancellationToken ct)
        {
            return declaration.Variables.Select(v => model.GetDeclaredSymbol(v, ct)).Where(local => local != null).ToList();
        }

        // The local, parameter or field "using (stream = File.OpenRead(path))" assigns.
        private static List<ISymbol> Assigned(SemanticModel model, ExpressionSyntax expression, CancellationToken ct)
        {
            List<ISymbol> assigned = new List<ISymbol>();
            if (!(expression is AssignmentExpressionSyntax assignment) || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || !(assignment.Left is IdentifierNameSyntax name)) return assigned;
            ISymbol symbol = model.GetSymbolInfo(name, ct).Symbol;
            if (symbol is ILocalSymbol || symbol is IParameterSymbol || symbol is IFieldSymbol) assigned.Add(symbol);
            return assigned;
        }

        private static void Report(SyntaxNodeAnalysisContext context, SyntaxToken usingKeyword, List<ISymbol> disposed, IEnumerable<StatementSyntax> scope)
        {
            HashSet<string> names = new HashSet<string>(disposed.Select(symbol => symbol.Name));
            if (names.Count == 0) return;
            StatementSyntax[] statements = scope.ToArray();
            foreach (ISymbol variable in disposed.ToArray())
            {
                if (statements.SelectMany(statement => statement.DescendantNodesAndSelf()).OfType<IdentifierNameSyntax>().Any(name =>
                    names.Contains(name.Identifier.ValueText) && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, variable)
                    && VariableWrites.IsWrite(context.SemanticModel.GetOperation(name, context.CancellationToken)))) disposed.Remove(variable);
            }
            foreach (StatementSyntax statement in statements)
            {
                foreach (SyntaxNode node in statement.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax) && !(child is LocalFunctionStatementSyntax)))
                {
                    if (!(node is ReturnStatementSyntax returned) || !(Unwrap(returned.Expression) is IdentifierNameSyntax name) || !names.Contains(name.Identifier.ValueText)) continue;
                    ISymbol symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
                    if (!disposed.Contains(symbol, SymbolEqualityComparer.Default)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, usingKeyword.GetLocation(), symbol.Name));
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
