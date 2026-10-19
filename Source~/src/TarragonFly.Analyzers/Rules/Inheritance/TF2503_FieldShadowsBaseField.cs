using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2503: an instance field with the same name as a field of a base class that the derived class
    // can see.
    //
    // Base code keeps using its own field, so a value set in the derived class never reaches it. Unity
    // also refuses a serialized field name that a base class already uses ("The same field name is
    // serialized multiple times"), and the Inspector loses the value.
    //
    // A field marked "new" was hidden on purpose and is fine, as are static fields and private base
    // fields. Reported on the field name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldShadowsBaseFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2503",
            "Field hides a base class field",
            "'{0}' hides the field of the same name in '{1}'; rename it",
            "Inheritance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax declaration = (FieldDeclarationSyntax)context.Node;
                foreach (SyntaxToken modifier in declaration.Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.NewKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword)) return;
                }
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (!(context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol field)) continue;
                    IFieldSymbol hidden = BaseFields.Find(field, StringComparison.Ordinal);
                    if (hidden != null) context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), field.Name, hidden.ContainingType.Name));
                }
            }), SyntaxKind.FieldDeclaration);
        }
    }
}