using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2046: base.GetHashCode() or base.Equals(...) called from a GetHashCode or Equals override when
    // no base class overrides them, so the call goes to object's versions.
    //
    // Object's versions use reference identity, so mixing them in makes two equal values hash or
    // compare differently, and the override stops working as value equality.
    //
    // Only calls inside the GetHashCode and Equals overrides are checked, and structs, whose base is
    // ValueType, aren't. "if (base.Equals(other)) return true;" is fine here: with object as the base
    // it is a correct same-object shortcut. Reported on the call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BaseObjectHashCodeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2046",
            "object's GetHashCode or Equals used in an override",
            "base.{0} here is object's reference-based version; leave it out of value equality",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !(access.Expression is BaseExpressionSyntax)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "GetHashCode" && name != "Equals") return;

                MethodDeclarationSyntax method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (method == null || (method.Identifier.ValueText != "GetHashCode" && method.Identifier.ValueText != "Equals")) return;
                if (!method.Modifiers.Any(SyntaxKind.OverrideKeyword) || BaseEqualsAsIdentityCheckAnalyzer.IsSameObjectShortcut(invocation)) return;
                if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called
                    && called.ContainingType.SpecialType == SpecialType.System_Object)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), name + "()"));
                }
            }), SyntaxKind.InvocationExpression);
        }
    }
}