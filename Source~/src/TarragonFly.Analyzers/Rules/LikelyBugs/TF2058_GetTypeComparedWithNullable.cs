using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2058: GetType() compared with a nullable type, as in "value.GetType() == typeof(int?)".
    //
    // Calling GetType() boxes a nullable into its underlying type (or throws when it has no value), so
    // GetType() never returns Nullable<T> and the comparison always gives the same answer.
    //
    // Both == and != count, in either order, with GetType() called on something or bare, on this
    // object. Only the built-in GetType counts, not a GetType method a game type declares itself.
    // Reported on the whole comparison.
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
                if ((IsGetTypeCall(context, comparison.Left) && IsNullableTypeOf(context, comparison.Right))
                    || (IsGetTypeCall(context, comparison.Right) && IsNullableTypeOf(context, comparison.Left)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation()));
                }
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        // object.GetType(), as "x.GetType()" or a bare "GetType()".
        private static bool IsGetTypeCall(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (!(expression is InvocationExpressionSyntax invocation) || invocation.ArgumentList.Arguments.Count != 0) return false;
            SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name : invocation.Expression as IdentifierNameSyntax;
            if (name == null || name.Identifier.ValueText != "GetType") return false;
            return context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method
                && (method.ContainingType.SpecialType == SpecialType.System_Object || SymbolNames.IsNamed(method.ContainingType, "System.Type"));
        }

        private static bool IsNullableTypeOf(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (!(expression is TypeOfExpressionSyntax typeOf)) return false;
            ITypeSymbol type = context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type;
            return type != null && type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }
    }
}