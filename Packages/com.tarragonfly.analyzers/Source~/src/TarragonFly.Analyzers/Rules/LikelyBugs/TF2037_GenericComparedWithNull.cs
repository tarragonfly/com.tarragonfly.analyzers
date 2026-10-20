using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2037: a value of a type parameter that may be a struct compared with null, as in "if (item ==
    // null)" in Inventory<T>.
    //
    // For a struct the check is always false, so Inventory<int> never sees an "empty" slot. Use
    // EqualityComparer<T>.Default.Equals(item, default), or constrain T to class.
    //
    // A class constraint, or one to a class type such as ScriptableObject, makes the check fine.
    // Interface constraints don't help, since structs can implement interfaces. "is null" isn't
    // reported. Reported on the null.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class GenericComparedWithNullAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2037",
            "Unconstrained generic compared with null",
            "'{0}' may be a struct, which is never null; compare with default or constrain '{0}' to class",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                ExpressionSyntax nullSide = comparison.Right.IsKind(SyntaxKind.NullLiteralExpression) ? comparison.Right
                    : comparison.Left.IsKind(SyntaxKind.NullLiteralExpression) ? comparison.Left : null;
                if (nullSide == null) return;
                ExpressionSyntax other = nullSide == comparison.Right ? comparison.Left : comparison.Right;
                if (context.SemanticModel.GetTypeInfo(other, context.CancellationToken).Type is ITypeParameterSymbol parameter
                    && !parameter.IsReferenceType && !parameter.IsValueType)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, nullSide.GetLocation(), parameter.Name));
                }
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }
    }
}