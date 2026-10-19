using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7000: a static field that isn't const, readonly or private.
    //
    // Any code can change it, and with domain reload turned off (Enter Play Mode Options) its value
    // also survives from one Play Mode session into the next, so the game starts in whatever state the
    // last run left behind.
    //
    // Protected and internal fields count, also inside private or internal classes. Static properties
    // and events aren't checked. Reported on each field name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class VisibleStaticFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7000",
            "Non-private static field that isn't constant",
            "Make '{0}' private or readonly; any code can change it, and with domain reload off the value carries over into the next Play Mode session",
            "StaticState",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax field = (FieldDeclarationSyntax)context.Node;
                SyntaxTokenList modifiers = field.Modifiers;
                if (!modifiers.Any(SyntaxKind.StaticKeyword) || modifiers.Any(SyntaxKind.ReadOnlyKeyword) || modifiers.Any(SyntaxKind.ConstKeyword)) return;
                bool hasAccessibility = modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword) || modifiers.Any(SyntaxKind.InternalKeyword);
                if (!hasAccessibility) return;
                foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), variable.Identifier.ValueText));
                }
            }), SyntaxKind.FieldDeclaration);
        }
    }
}