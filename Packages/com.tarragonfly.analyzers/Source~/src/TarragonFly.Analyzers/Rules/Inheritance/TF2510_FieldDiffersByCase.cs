using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2510: an instance field whose name differs from a base class field only by case, such as
    // "Health" next to an inherited "health".
    //
    // Which one a line means depends on one letter, and Unity's Inspector shows both with the same
    // label.
    //
    // Only instance fields the derived class can see count. Reported on the field name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldDiffersByCaseAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2510",
            "Field differs from a base class field only by case",
            "'{0}' differs from '{1}' in '{2}' only by case; rename it",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax declaration = (FieldDeclarationSyntax)context.Node;
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (!(context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol field) || field.IsStatic || field.IsConst) continue;
                    IFieldSymbol other = BaseFields.Find(context.Compilation, field, StringComparison.OrdinalIgnoreCase);
                    if (other == null || other.Name == field.Name) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), field.Name, other.Name, other.ContainingType.Name));
                }
            }), SyntaxKind.FieldDeclaration);
        }
    }
}
