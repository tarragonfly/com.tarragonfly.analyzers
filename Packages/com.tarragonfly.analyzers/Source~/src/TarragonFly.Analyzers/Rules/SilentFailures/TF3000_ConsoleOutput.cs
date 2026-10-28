using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;
using TarragonFly.Analyzers.Rules.Culture;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3000: Console.Write or Console.WriteLine in game code.
    //
    // Unity doesn't show standard output: the text never reaches the Console window or the player
    // log, so the message is lost. Use Debug.Log, or a logger the project already has.
    //
    // Console.Out, Console.Error and reading from the console aren't checked. Reported on the call
    // name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ConsoleOutputAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3000",
            "Console output in a Unity game",
            "Console.{0} never reaches the Unity Console or the player log; use Debug.Log",
            "SilentFailures",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                string name = CultureCalls.CalledName(invocation)?.Identifier.ValueText;
                if (name != "Write" && name != "WriteLine") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (SymbolNames.IsNamed(method.ContainingType, "System.Console"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), name));
                }
            }), SyntaxKind.InvocationExpression);
        }
    }
}
