using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2047: &, | or ^ on an enum without [Flags], as in "Team.Red | Team.Blue".
    //
    // Its values are numbered 0, 1, 2..., so combining them gives another member (Red | Blue is Blue
    // when Red is 0) or an unnamed value. Add [Flags] with power-of-two values, or use a collection.
    //
    // Compound &=, |= and ^= count; ~ and comparisons don't. Reported on the operator.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BitwiseNonFlagsEnumAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2047",
            "Bitwise operation on a non-[Flags] enum",
            "'{0}' isn't a [Flags] enum, so combining its values gives another member or a nameless value",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax left;
                SyntaxToken operatorToken;
                switch (context.Node)
                {
                    case BinaryExpressionSyntax binary: left = binary.Left; operatorToken = binary.OperatorToken; break;
                    case AssignmentExpressionSyntax assignment: left = assignment.Left; operatorToken = assignment.OperatorToken; break;
                    default: return;
                }
                if (context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type is INamedTypeSymbol type
                    && type.TypeKind == TypeKind.Enum && !FlagsEnums.IsFlags(type)
                    && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetTypeInfo(left, context.CancellationToken).Type, type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, operatorToken.GetLocation(), type.Name));
                }
            }), SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression,
                SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression);
        }
    }
}