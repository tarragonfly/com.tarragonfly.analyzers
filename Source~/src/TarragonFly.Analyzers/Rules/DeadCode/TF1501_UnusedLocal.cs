using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1501: a local variable that is never used after its declaration: not read, not written, not
    // passed anywhere.
    //
    // Leftovers like "Vector3 offset = GetOffset();" make readers look for where the value goes.
    //
    // Checked: locals with a computed value, local constants, pattern variables ("is Enemy enemy"),
    // out variables and deconstruction variables. Skipped: foreach and for variables, using
    // variables (disposing is their use), and variables named "_".
    //
    // Locals declared without a value, locals given a constant value and unused catch variables are
    // left to the C# compiler, which already warns about them (CS0168, CS0219) in the Unity Console.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnusedLocalAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1501",
            "Unused local variable",
            "Remove the unused local '{0}'",
            "DeadCode",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken name = CheckedName(context);
                if (name.IsKind(SyntaxKind.None) || name.ValueText == "_") return;
                if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is ILocalSymbol local)) return;

                SyntaxNode scopeNode = context.Node.FirstAncestorOrSelf<MemberDeclarationSyntax>() ?? context.Node.SyntaxTree.GetRoot(context.CancellationToken);
                foreach (SyntaxNode node in scopeNode.DescendantNodes())
                {
                    if (!(node is IdentifierNameSyntax identifier) || identifier.Identifier.ValueText != name.ValueText) continue;
                    if (SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol, local)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.ValueText));
            }), SyntaxKind.VariableDeclarator, SyntaxKind.SingleVariableDesignation);
        }

        // The declared name when this declaration is checked, default otherwise.
        private static SyntaxToken CheckedName(SyntaxNodeAnalysisContext context)
        {
            if (context.Node is SingleVariableDesignationSyntax designation) return designation.Identifier;

            VariableDeclaratorSyntax declarator = (VariableDeclaratorSyntax)context.Node;
            if (!(declarator.Parent?.Parent is LocalDeclarationStatementSyntax statement) || !statement.UsingKeyword.IsKind(SyntaxKind.None)) return default;
            if (statement.IsConst) return declarator.Identifier;

            // The compiler already warns about these two (CS0168, CS0219).
            if (declarator.Initializer == null) return default;
            if (context.SemanticModel.GetConstantValue(declarator.Initializer.Value, context.CancellationToken).HasValue) return default;
            return declarator.Identifier;
        }
    }
}