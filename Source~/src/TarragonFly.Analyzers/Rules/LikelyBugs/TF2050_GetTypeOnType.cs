using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2050: GetType() called on something that is already a System.Type, as in
    // "typeof(Enemy).GetType()" or "component.GetType().GetType()".
    //
    // The result is always typeof(RuntimeType), never the type meant, so lookups and comparisons
    // built on it silently fail.
    //
    // Only object.GetType(), and the one Type declares over it, count, not a GetType method a game
    // type declares itself, and "this.GetType()" inside a class derived from Type is fine: there it
    // names that class. Reported from the "." to the end of the call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class GetTypeOnTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2050",
            "GetType() on a System.Type",
            "This is already a Type; GetType() on it gives the Type class itself, not the type meant",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "GetType" || invocation.ArgumentList.Arguments.Count != 0) return;
                if (access.Expression is ThisExpressionSyntax || access.Expression is BaseExpressionSyntax) return;
                if (!SymbolNames.IsOrDerivesFrom(context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type, "System.Type")) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !IsBuiltInGetType(method)) return;
                TextSpan span = TextSpan.FromBounds(access.OperatorToken.SpanStart, invocation.Span.End);
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(invocation.SyntaxTree, span)));
            }), SyntaxKind.InvocationExpression);
        }

        // object.GetType(), or the one System.Type declares over it.
        private static bool IsBuiltInGetType(IMethodSymbol method)
        {
            return method.ContainingType.SpecialType == SpecialType.System_Object || SymbolNames.IsNamed(method.ContainingType, "System.Type");
        }
    }
}