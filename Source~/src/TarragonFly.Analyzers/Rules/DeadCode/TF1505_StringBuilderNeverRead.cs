using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1505: a local StringBuilder that is filled but never read, as in a debug summary built with
    // Append calls whose ToString() was deleted.
    //
    // Every Append allocates and copies text for nothing.
    //
    // A local created with new StringBuilder(...) counts when every use only changes it: Append,
    // AppendLine, AppendFormat, AppendJoin, Insert, Replace, Remove and Clear, alone or chained. Any
    // other use reads it: ToString(), Length, passing it on, returning or storing it. Fields aren't
    // checked. Reported on the variable. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringBuilderNeverReadAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1505",
            "StringBuilder filled but never read",
            "'{0}' is built up but nothing reads it; remove it or use its text",
            "DeadCode",
            CodeKind.Game);

        private static readonly HashSet<string> Changes = new HashSet<string>
        {
            "Append", "AppendLine", "AppendFormat", "AppendJoin", "Insert", "Replace", "Remove", "Clear"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                VariableDeclaratorSyntax declarator = (VariableDeclaratorSyntax)context.Node;
                if (!(declarator.Initializer?.Value is BaseObjectCreationExpressionSyntax) || !(declarator.Parent?.Parent is LocalDeclarationStatementSyntax)) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is ILocalSymbol local)) return;
                if (!SymbolNames.IsNamed(local.Type, "System.Text.StringBuilder")) return;
                SyntaxNode body = declarator.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax || node is AccessorDeclarationSyntax || node is LocalFunctionStatementSyntax || node is AnonymousFunctionExpressionSyntax);
                if (body == null) return;

                foreach (IdentifierNameSyntax name in body.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (name.Identifier.ValueText != local.Name) continue;
                    if (!SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, local)) continue;
                    if (!OnlyChanges(name)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, declarator.GetLocation(), local.Name));
            }), SyntaxKind.VariableDeclarator);
        }

        // "builder.Append(...)", possibly chained, as a statement whose result nobody uses.
        private static bool OnlyChanges(ExpressionSyntax expression)
        {
            while (true)
            {
                if (!(expression.Parent is MemberAccessExpressionSyntax access) || access.Expression != expression || !Changes.Contains(access.Name.Identifier.ValueText)) return false;
                if (!(access.Parent is InvocationExpressionSyntax call)) return false;
                if (call.Parent is ExpressionStatementSyntax) return true;
                expression = call;
            }
        }
    }
}