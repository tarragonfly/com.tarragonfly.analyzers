using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2036: a property or field set through a readonly field whose type is a type parameter that may
    // be a struct, as in "stats.Health = 5" with "readonly T stats".
    //
    // When T is a struct, reading a readonly field makes a copy, so the assignment changes the copy and
    // the stored value never changes.
    //
    // Type parameters constrained to class, or to a class type such as ScriptableObject, are fine, as
    // are assignments in constructors, where the field itself is still writable. Concrete struct types
    // aren't checked, since the C# compiler already rejects that (CS1648). Reported on the assigned
    // member.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReadonlyFieldCopyChangedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2036",
            "Change made to a copy of a readonly field",
            "If '{0}' is a struct this changes a copy and the field keeps its old value; drop readonly or constrain the type to class",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (!(assignment.Left is MemberAccessExpressionSyntax member)) return;
                if (!(context.SemanticModel.GetSymbolInfo(member.Expression, context.CancellationToken).Symbol is IFieldSymbol field) || !field.IsReadOnly) return;
                if (!(field.Type is ITypeParameterSymbol parameter) || parameter.IsReferenceType) return;
                if (assignment.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() != null) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, member.GetLocation(), field.Name));
            }), SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression);
        }
    }
}