using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2058: GetType() compared with a nullable type, as in "value.GetType() == typeof(int?)".
    //
    // Calling GetType() boxes a nullable into its underlying type (or throws when it has no value), so
    // GetType() never returns Nullable<T> and the comparison always gives the same answer.
    //
    // Both == and != count, in either order. Reported on the whole comparison.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class GetTypeComparedWithNullableAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2058",
            "GetType() compared with a nullable type",
            "GetType() never returns a Nullable<T> type, so this comparison always has the same result",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if ((IsGetTypeCall(comparison.Left) && IsNullableTypeOf(context, comparison.Right))
                    || (IsGetTypeCall(comparison.Right) && IsNullableTypeOf(context, comparison.Left)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation()));
                }
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        private static bool IsGetTypeCall(ExpressionSyntax expression)
        {
            return expression is InvocationExpressionSyntax invocation && invocation.ArgumentList.Arguments.Count == 0
                && invocation.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "GetType";
        }

        private static bool IsNullableTypeOf(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (!(expression is TypeOfExpressionSyntax typeOf)) return false;
            ITypeSymbol type = context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type;
            return type != null && type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }
    }
}