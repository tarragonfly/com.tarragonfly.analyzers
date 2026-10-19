using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2025: Enumerable.Sum over int or long values inside an unchecked block or expression.
    //
    // Sum always checks for overflow and throws OverflowException, whatever the surrounding context
    // says, so the unchecked doesn't do what it looks like it does here.
    //
    // Float, double and decimal sums aren't checked, since they don't overflow that way. A checked
    // context closer to the call wins. Reported on the Sum name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UncheckedSumAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2025",
            "Enumerable.Sum inside unchecked",
            "Sum still throws OverflowException inside unchecked; add the values in a loop if overflow is expected",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "Sum") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!SymbolNames.IsNamed((method.ReducedFrom ?? method).ContainingType, "System.Linq.Enumerable") || !IsIntegral(method.ReturnType)) return;
                if (IsInUncheckedContext(invocation)) context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation()));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool IsInUncheckedContext(SyntaxNode node)
        {
            for (SyntaxNode ancestor = node.Parent; ancestor != null && !(ancestor is MemberDeclarationSyntax); ancestor = ancestor.Parent)
            {
                if (ancestor.IsKind(SyntaxKind.UncheckedStatement) || ancestor.IsKind(SyntaxKind.UncheckedExpression)) return true;
                if (ancestor.IsKind(SyntaxKind.CheckedStatement) || ancestor.IsKind(SyntaxKind.CheckedExpression)) return false;
            }
            return false;
        }

        private static bool IsIntegral(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) type = named.TypeArguments[0];
            return type?.SpecialType == SpecialType.System_Int32 || type?.SpecialType == SpecialType.System_Int64;
        }
    }
}