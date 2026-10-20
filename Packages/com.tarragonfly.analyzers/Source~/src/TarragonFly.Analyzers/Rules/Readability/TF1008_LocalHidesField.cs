using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1008: a local variable with the same name as a field, property or event of its own type.
    //
    // "health = 5;" a few lines later then sets the local, not the field, which is a classic source
    // of "the value never changes" bugs.
    //
    // Locals include for, foreach, using and catch variables, out variables, pattern and deconstruction
    // variables. Lambda and local function parameters aren't checked, nor are inherited members, nor
    // instance members hidden inside a static method, where they can't be used anyway. Names must match
    // exactly, case included. Reported on the local's name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LocalHidesFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1008",
            "Local variable hides a member",
            "This local hides the {0} '{1}'; rename one of them",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken name = LocalName(context.Node);
                if (name.IsKind(SyntaxKind.None)) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is ILocalSymbol local)) return;

                INamedTypeSymbol type = local.ContainingType;
                ISymbol container = local.ContainingSymbol;
                while (container is IMethodSymbol method && (method.MethodKind == MethodKind.LocalFunction || method.MethodKind == MethodKind.AnonymousFunction))
                {
                    container = container.ContainingSymbol;
                }
                if (type == null) return;

                foreach (ISymbol member in type.GetMembers(name.ValueText))
                {
                    if (!(member is IFieldSymbol || member is IPropertySymbol || member is IEventSymbol) || member.IsImplicitlyDeclared) continue;
                    if (container != null && container.IsStatic && !member.IsStatic) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), Kind(member), member.Name));
                    return;
                }
            }), SyntaxKind.VariableDeclarator, SyntaxKind.ForEachStatement, SyntaxKind.CatchDeclaration, SyntaxKind.SingleVariableDesignation);
        }

        private static SyntaxToken LocalName(SyntaxNode node)
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator when !(declarator.Parent?.Parent is FieldDeclarationSyntax) && !(declarator.Parent?.Parent is EventFieldDeclarationSyntax):
                    return declarator.Identifier;
                case ForEachStatementSyntax forEach: return forEach.Identifier;
                case CatchDeclarationSyntax catchDeclaration: return catchDeclaration.Identifier;
                case SingleVariableDesignationSyntax designation: return designation.Identifier;
                default: return default;
            }
        }

        private static string Kind(ISymbol member)
        {
            switch (member)
            {
                case IFieldSymbol _: return "field";
                case IPropertySymbol _: return "property";
                default: return "event";
            }
        }
    }
}