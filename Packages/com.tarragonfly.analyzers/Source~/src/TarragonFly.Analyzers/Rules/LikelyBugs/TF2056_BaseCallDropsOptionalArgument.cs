using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2056: an override passing on to the base method of the same name but leaving out one of its
    // optional arguments, as in "base.TakeDamage(amount)" inside "override void TakeDamage(int amount,
    // bool critical = false)".
    //
    // The base then uses its own default instead of the value the caller passed to the override, so a
    // critical hit silently turns into a normal one.
    //
    // Only calls to base.SameName from inside an override are checked. Reported on the call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BaseCallDropsOptionalArgumentAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2056",
            "Base call leaves out an optional argument",
            "Pass '{0}' on to the base method; leaving it out uses the base default instead of the caller's value",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !(access.Expression is BaseExpressionSyntax)) return;
                MethodDeclarationSyntax method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (method == null || !method.Modifiers.Any(SyntaxKind.OverrideKeyword) || method.Identifier.ValueText != access.Name.Identifier.ValueText) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called)) return;

                HashSet<IParameterSymbol> passed = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
                for (int i = 0; i < invocation.ArgumentList.Arguments.Count; i++)
                {
                    ArgumentSyntax argument = invocation.ArgumentList.Arguments[i];
                    if (argument.NameColon != null)
                    {
                        foreach (IParameterSymbol parameter in called.Parameters)
                        {
                            if (parameter.Name == argument.NameColon.Name.Identifier.ValueText) passed.Add(parameter);
                        }
                    }
                    else if (i < called.Parameters.Length)
                    {
                        passed.Add(called.Parameters[i]);
                    }
                }
                foreach (IParameterSymbol parameter in called.Parameters)
                {
                    if (parameter.IsOptional && !passed.Contains(parameter))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), parameter.Name));
                        return;
                    }
                }
            }), SyntaxKind.InvocationExpression);
        }
    }
}