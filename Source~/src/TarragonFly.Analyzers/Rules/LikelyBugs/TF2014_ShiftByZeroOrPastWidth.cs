using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2014: a shift by a constant at least as large as the number of bits, as in "layerMask << 32" on
    // an int, or by a constant 0.
    //
    // C# only uses the low bits of the count, so "1 << 32" is 1, not 0, and the mask quietly wraps. A
    // shift by 0 does nothing, which usually means the wrong count was typed.
    //
    // The width is 32 bits for int and uint (and smaller types, which are widened to int) and 64 for
    // long and ulong. Compound <<= and >>= count. A shift by 0 is fine in a list of masks: when the
    // statement or member next to it, or its own statement, also shifts ("A = 1 << 0; B = 1 << 1;"),
    // and in enum member values. Negative counts aren't reported. Reported on the whole shift.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ShiftByZeroOrPastWidthAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2014",
            "Shift by zero or past the bit width",
            "{0}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax value;
                ExpressionSyntax count;
                switch (context.Node)
                {
                    case BinaryExpressionSyntax binary: value = binary.Left; count = binary.Right; break;
                    case AssignmentExpressionSyntax assignment: value = assignment.Left; count = assignment.Right; break;
                    default: return;
                }
                Optional<object> constant = context.SemanticModel.GetConstantValue(count, context.CancellationToken);
                if (!constant.HasValue || !(constant.Value is int shift)) return;

                if (shift == 0)
                {
                    if (!IsInMaskList(context.Node)) context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), "Shifting by 0 does nothing; check the count"));
                    return;
                }

                int width = Width(context.SemanticModel.GetTypeInfo(value, context.CancellationToken).Type);
                if (width == 0 || shift < width) return;
                string message = "Shifting a " + width + "-bit value by " + shift + " wraps around to a shift by " + (shift % width);
                context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), message));
            }), SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression, SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression);
        }

        // True when the shift is an enum value, or its statement or member, or the one before or after
        // it, holds another shift, as in a list of masks starting at "1 << 0".
        private static bool IsInMaskList(SyntaxNode shift)
        {
            if (shift.FirstAncestorOrSelf<EnumMemberDeclarationSyntax>() != null) return true;
            SyntaxNode unit = shift.Ancestors().FirstOrDefault(node => node is StatementSyntax || node is MemberDeclarationSyntax);
            if (unit == null) return false;
            if (unit.DescendantNodes().Any(node => node != shift && IsShift(node))) return true;

            SyntaxNode[] siblings = unit.Parent.ChildNodes().Where(node => node is StatementSyntax || node is MemberDeclarationSyntax).ToArray();
            int index = System.Array.IndexOf(siblings, unit);
            return (index > 0 && HasShift(siblings[index - 1])) || (index < siblings.Length - 1 && HasShift(siblings[index + 1]));
        }

        private static bool HasShift(SyntaxNode node)
        {
            return node.DescendantNodesAndSelf().Any(IsShift);
        }

        private static bool IsShift(SyntaxNode node)
        {
            return node.IsKind(SyntaxKind.LeftShiftExpression) || node.IsKind(SyntaxKind.RightShiftExpression)
                || node.IsKind(SyntaxKind.LeftShiftAssignmentExpression) || node.IsKind(SyntaxKind.RightShiftAssignmentExpression);
        }

        private static int Width(ITypeSymbol type)
        {
            switch (type?.SpecialType)
            {
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return 64;
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Char:
                    return 32;
                default:
                    return 0;
            }
        }
    }
}