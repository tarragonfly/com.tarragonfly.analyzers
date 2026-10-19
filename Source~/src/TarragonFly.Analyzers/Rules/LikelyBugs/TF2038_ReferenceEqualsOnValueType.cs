using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2038: object.ReferenceEquals with a struct argument, such as a Vector3.
    //
    // Each struct argument gets boxed into a new object, so the result is always false, even for the
    // same value.
    //
    // Type parameters constrained to struct count; unconstrained ones and nullable structs don't.
    // Reported on the ReferenceEquals name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReferenceEqualsOnValueTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2038",
            "ReferenceEquals on a value type",
            "ReferenceEquals boxes the struct '{0}' and is always false; compare with == or Equals",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (method.Name != "ReferenceEquals" || method.ContainingType.SpecialType != SpecialType.System_Object) return;
                foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
                {
                    ITypeSymbol type = context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type;
                    if (type == null || !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), type.Name));
                    return;
                }
            }), SyntaxKind.InvocationExpression);
        }
    }
}