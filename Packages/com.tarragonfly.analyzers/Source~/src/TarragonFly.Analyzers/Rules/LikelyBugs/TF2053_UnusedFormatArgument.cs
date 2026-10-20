using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2053: a composite format call with an argument no placeholder uses, as in string.Format("{0}
    // hp", health, armor) or string.Format("Done", score), or with no arguments and no placeholders at
    // all, as in string.Format("Done").
    //
    // The value never shows up, so a placeholder is missing or the wrong one is used. Without any
    // arguments the format call does nothing but unescape braces.
    //
    // Same calls and limits as TF2024; formats that would throw are TF2024's job. A placeholder used
    // twice is fine. Reported on the called method, as in "string.Format".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnusedFormatArgumentAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2053",
            "Format argument no placeholder uses",
            "{0}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                CompositeFormatCall call = CompositeFormatCall.Read(context.SemanticModel, invocation);
                if (call == null || !CompositeFormatCall.TryParse(call.Format, out List<int> indexes)) return;

                if (call.ArgumentCount == 0 && indexes.Count == 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), "Nothing is formatted here; use the string itself"));
                    return;
                }

                HashSet<int> used = new HashSet<int>(indexes);
                for (int argument = 0; argument < call.ArgumentCount; argument++)
                {
                    if (used.Contains(argument)) continue;
                    foreach (int index in indexes)
                    {
                        if (index >= call.ArgumentCount) return;
                    }
                    string message = "Argument " + argument + " is never used by the format string; add its placeholder or remove it";
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), message));
                    return;
                }
            }), SyntaxKind.InvocationExpression);
        }
    }
}