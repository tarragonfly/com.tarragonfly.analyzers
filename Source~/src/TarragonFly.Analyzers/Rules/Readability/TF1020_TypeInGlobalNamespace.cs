using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1020: a class, struct, interface, enum, record or delegate declared outside any namespace.
    //
    // Unity puts every script without an assembly definition into one Assembly-CSharp, next to Asset
    // Store plugins that also love names like Player, GameManager or Utils; a namespace keeps them
    // apart.
    //
    // Nested types aren't reported separately. Delegates count too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TypeInGlobalNamespaceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1020",
            "Type in the global namespace",
            "Move '{0}' into a namespace so it can't clash with plugin types",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                if (!(context.Node.Parent is CompilationUnitSyntax)) return;
                SyntaxToken name = context.Node is BaseTypeDeclarationSyntax type ? type.Identifier : ((DelegateDeclarationSyntax)context.Node).Identifier;
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.ValueText));
            }), SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.EnumDeclaration,
                SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration, SyntaxKind.DelegateDeclaration);
        }
    }
}