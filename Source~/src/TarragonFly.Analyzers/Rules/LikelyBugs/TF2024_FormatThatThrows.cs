using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2024: a composite format string that throws FormatException at runtime: a placeholder past the
    // last argument ("{1}" with one argument), or a malformed one such as "{0", "{0}}", "{-1}",
    // "{name}", "{ 0}", "{0,}" or "{0,+5}".
    //
    // The exception only happens when that line runs, often in a rarely seen error or debug message.
    //
    // Checked calls: string.Format, Console.Write and WriteLine, TextWriter, StringBuilder.AppendFormat
    // and any method with a "format" string parameter followed by object arguments. The format must
    // be a constant or a local only ever set to one, not reassigned or passed by ref or out.
    // Arguments passed as an explicit object array aren't checked; named arguments in any order are,
    // and so is an int[] passed alone, which is a single argument. Items are read the way the runtime
    // reads them, so "{0:}" and "{0 }" are fine. Unity's Debug.LogFormat family counts too. Reported
    // on the called method, as in "string.Format".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FormatThatThrowsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2024",
            "Composite format string that throws",
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
                if (call == null) return;

                string problem = null;
                if (!CompositeFormatCall.TryParse(call.Format, out List<int> indexes)) problem = "This format string is malformed and throws FormatException";
                else if (indexes.Count > 0 && Max(indexes) >= call.ArgumentCount)
                {
                    problem = $"This format uses {{{Max(indexes)}}} but only {call.ArgumentCount} argument(s) follow, so it throws FormatException";
                }
                if (problem != null) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), problem));
            }), SyntaxKind.InvocationExpression);
        }

        private static int Max(List<int> indexes)
        {
            int max = 0;
            foreach (int index in indexes)
            {
                if (index > max) max = index;
            }
            return max;
        }
    }
}