using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7005: a static field initializer that reads a static field declared further down, as in "static
    // readonly int MaxHealth = BaseHealth * 2;" above "static readonly int BaseHealth = 50;".
    //
    // Static initializers run top to bottom, so BaseHealth is still 0 when MaxHealth is computed, and
    // MaxHealth silently ends up 0. Reordering the fields, or making them const, fixes it.
    //
    // Only fields of the same type, declared later in the same file, count. Constants are fine, as
    // are reads inside lambdas and methods, which run later, and nameof(Field), which doesn't read the
    // value. Reported on the "= ..." part.
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
            foreach (SyntaxNode node in variable.Initializer.Value.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax) && !IsNameOf(child)))
            {
                if (!(node is IdentifierNameSyntax name)) continue;
                if (!(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IFieldSymbol read) || !read.IsStatic || read.IsConst) continue;
                if (!SymbolEqualityComparer.Default.Equals(read.ContainingType, field.ContainingType)) continue;
                foreach (SyntaxReference reference in read.DeclaringSyntaxReferences)
                {
                    if (reference.SyntaxTree == variable.SyntaxTree && reference.Span.Start > variable.Span.Start) return read;
                }
            }
            return null;
        }

        private static bool IsNameOf(SyntaxNode node)
        {
            return node is InvocationExpressionSyntax invocation && invocation.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "nameof";
        }
    }
}