using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2051: a shift on a dynamic value by something that isn't an int, as in "value << 1L".
    //
    // With dynamic the compiler can't check the shift count, so a long, double, string or null count
    // throws RuntimeBinderException when the line runs.
    //
    // Counts that convert to int implicitly (byte, short, char) are fine. Compound <<= and >>= count.
    // Reported on the count.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DynamicShiftCountAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2051",
            "Shift count that isn't an int",
            "A shift count must convert to int; this one throws RuntimeBinderException at runtime",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax value = context.Node is BinaryExpressionSyntax binary ? binary.Left : ((AssignmentExpressionSyntax)context.Node).Left;
                ExpressionSyntax count = context.Node is BinaryExpressionSyntax shift ? shift.Right : ((AssignmentExpressionSyntax)context.Node).Right;
                if (context.SemanticModel.GetTypeInfo(value, context.CancellationToken).Type?.TypeKind != TypeKind.Dynamic) return;

                bool invalid = count.IsKind(SyntaxKind.NullLiteralExpression);
                if (!invalid)
                {
                    ITypeSymbol countType = context.SemanticModel.GetTypeInfo(count, context.CancellationToken).Type;
                    ITypeSymbol intType = context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                    invalid = countType != null && countType.TypeKind != TypeKind.Dynamic && !context.SemanticModel.Compilation.ClassifyConversion(countType, intType).IsImplicit;
                }
                if (invalid) context.ReportDiagnostic(Diagnostic.Create(Rule, count.GetLocation()));
            }), SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression, SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression);
        }
    }
}