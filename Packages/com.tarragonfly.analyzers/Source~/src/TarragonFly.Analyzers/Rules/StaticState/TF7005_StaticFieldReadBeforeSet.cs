using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Rules.DeadCode;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7005: a static field initializer that reads a static field declared further down, as in "static
    // readonly int MaxHealth = BaseHealth * 2;" above "static readonly int BaseHealth = 50;".
    //
    // Static initializers run top to bottom, so BaseHealth is still 0 when MaxHealth is computed, and
    // MaxHealth silently ends up 0. Reordering the fields, or making them const, fixes it.
    //
    // Only fields of the same type, declared later in the same file, count. Constants are fine, as
    // are reads inside lambdas and methods, which run later, query clauses such as where and select,
    // which become lambdas (the first from's source and a join's source are read at once and count),
    // and nameof(Field), which doesn't read the value. Reported on the "= ..." part.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StaticFieldReadBeforeSetAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7005",
            "Static field read before it is initialized",
            "'{0}' is declared further down, so it still holds its default value when this initializer runs; move it above",
            "StaticState",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax declaration = (FieldDeclarationSyntax)context.Node;
                if (!declaration.Modifiers.Any(SyntaxKind.StaticKeyword) || declaration.Modifiers.Any(SyntaxKind.ConstKeyword)) return;
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (variable.Initializer == null) continue;
                    if (!(context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol field)) continue;
                    IFieldSymbol later = LaterFieldRead(context, variable, field);
                    if (later != null) context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Initializer.GetLocation(), later.Name));
                }
            }), SyntaxKind.FieldDeclaration);
        }

        private static IFieldSymbol LaterFieldRead(SyntaxNodeAnalysisContext context, VariableDeclaratorSyntax variable, IFieldSymbol field)
        {
            foreach (IdentifierNameSyntax name in NamesReadAtOnce(variable.Initializer.Value))
            {
                IOperation operation = context.SemanticModel.GetOperation(name, context.CancellationToken);
                if (operation is IFieldReferenceOperation fieldReference && FieldUses.IsPlainWrite(fieldReference)) continue;
                if (!(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IFieldSymbol read) || !read.IsStatic || read.IsConst) continue;
                if (!SymbolEqualityComparer.Default.Equals(read.ContainingType, field.ContainingType)) continue;
                foreach (SyntaxReference reference in read.DeclaringSyntaxReferences)
                {
                    if (reference.SyntaxTree == variable.SyntaxTree && reference.Span.Start > variable.Span.Start) return read;
                }
            }
            return null;
        }

        // The names the initializer reads while it runs, leaving out lambdas, nameof and the query
        // clauses that become lambdas.
        private static IEnumerable<IdentifierNameSyntax> NamesReadAtOnce(SyntaxNode node)
        {
            switch (node)
            {
                case AnonymousFunctionExpressionSyntax _:
                    yield break;
                case IdentifierNameSyntax name:
                    yield return name;
                    yield break;
                case QueryBodySyntax body:
                    foreach (JoinClauseSyntax join in body.Clauses.OfType<JoinClauseSyntax>())
                    {
                        foreach (IdentifierNameSyntax name in NamesReadAtOnce(join.InExpression)) yield return name;
                    }
                    if (body.Continuation == null) yield break;
                    foreach (IdentifierNameSyntax name in NamesReadAtOnce(body.Continuation.Body)) yield return name;
                    yield break;
            }
            if (NameOfExpressions.IsNameOf(node)) yield break;
            foreach (SyntaxNode child in node.ChildNodes())
            {
                foreach (IdentifierNameSyntax name in NamesReadAtOnce(child)) yield return name;
            }
        }
    }
}
