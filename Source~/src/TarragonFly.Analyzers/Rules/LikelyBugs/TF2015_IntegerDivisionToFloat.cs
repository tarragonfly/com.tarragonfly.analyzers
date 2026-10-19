using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2015: an integer division stored straight into a float, double or decimal, as in "float
    // progress = collected / total;".
    //
    // The division happens in integers first and drops the fraction, so progress is 0 until
    // collected reaches total. Cast one side first: "(float)collected / total".
    //
    // Checked where the result is converted directly: variable and field initializers, assignments and
    // method arguments. Returns and divisions inside a larger expression aren't checked. Reported on
    // the division.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IntegerDivisionToFloatAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2015",
            "Integer division stored in a floating-point value",
            "This divides integers, dropping the fraction, before converting to {0}; cast one side to {0} first",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax division = (BinaryExpressionSyntax)context.Node;
                if (!IsInteger(context.SemanticModel.GetTypeInfo(division.Left).Type) || !IsInteger(context.SemanticModel.GetTypeInfo(division.Right).Type)) return;

                SyntaxNode parent = division.Parent;
                bool convertedDirectly = parent is EqualsValueClauseSyntax || parent is ArgumentSyntax
                    || (parent is AssignmentExpressionSyntax assignment && assignment.Right == division);
                if (!convertedDirectly) return;

                ITypeSymbol target = context.SemanticModel.GetTypeInfo(division, context.CancellationToken).ConvertedType;
                switch (target?.SpecialType)
                {
                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                    case SpecialType.System_Decimal:
                        context.ReportDiagnostic(Diagnostic.Create(Rule, division.GetLocation(), target.ToDisplayString()));
                        break;
                }
            }), SyntaxKind.DivideExpression);
        }

        private static bool IsInteger(ITypeSymbol type)
        {
            switch (type?.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Char:
                    return true;
                default:
                    return false;
            }
        }
    }
}