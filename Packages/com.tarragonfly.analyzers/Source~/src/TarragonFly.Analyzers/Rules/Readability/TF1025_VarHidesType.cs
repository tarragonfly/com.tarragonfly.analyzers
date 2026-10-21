using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1025: a local, loop, using or out variable declared with 'var', as in "var enemy = Spawn();"
    // where "Enemy enemy = Spawn();" says what came back.
    //
    // With the type written out, a diff, a review or an agent reading one file knows what every
    // variable holds without opening the method it came from. Long generic types get noisy; that's
    // the accepted price, so the rule is off until the project turns it on.
    //
    // Off by default: report_var = game reports runtime and editor assemblies, all reports test
    // assemblies too. A type that can't be written, because it is or contains an anonymous type, is
    // never reported. Left alone: a deconstruction such as "var (a, b) = pair", a pattern such as
    // "is var x", "out var _" and a type that happens to be named var. Reported on the 'var' keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class VarHidesTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1025",
            "'var' hides the type",
            "Write the type instead of 'var'",
            "Readability",
            CodeKind.All,
            description: "Off until report_var is set in the settings file: game reports runtime and editor code, all reports test code too. " +
                "A type that can't be written, such as an anonymous type, is never reported.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            if ((scope.Settings.ReportVarIn & scope.AssemblyKind) == 0) return;

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeSyntax type = DeclaredType(context.Node);
                if (type == null || !type.IsVar) return;
                // A type named var is written out already.
                if (context.SemanticModel.GetSymbolInfo(type, context.CancellationToken).Symbol is ITypeSymbol named && named.Name == "var") return;
                if (!CanBeWritten(context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, type.GetLocation()));
            }), SyntaxKind.VariableDeclaration, SyntaxKind.ForEachStatement, SyntaxKind.DeclarationExpression);
        }

        // The written type of a local, for, using, fixed, foreach or out declaration; null for the
        // declaration shapes the rule leaves alone.
        private static TypeSyntax DeclaredType(SyntaxNode node)
        {
            switch (node)
            {
                case VariableDeclarationSyntax declaration:
                    return declaration.Type;
                case ForEachStatementSyntax loop:
                    return loop.Type;
                case DeclarationExpressionSyntax declaration:
                    bool isOutVariable = declaration.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
                    return isOutVariable && declaration.Designation is SingleVariableDesignationSyntax ? declaration.Type : null;
                default:
                    return null;
            }
        }

        // False for an anonymous type and for anything built on one, such as an array of them, a
        // tuple holding one or the IEnumerable a Select into one returns.
        private static bool CanBeWritten(ITypeSymbol type)
        {
            switch (type)
            {
                case null:
                case IErrorTypeSymbol _:
                    return false;
                case IArrayTypeSymbol array:
                    return CanBeWritten(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return CanBeWritten(pointer.PointedAtType);
                case INamedTypeSymbol named:
                    if (named.IsAnonymousType) return false;
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        if (!CanBeWritten(argument)) return false;
                    }
                    return named.ContainingType == null || CanBeWritten(named.ContainingType);
                default:
                    return true;
            }
        }
    }
}
