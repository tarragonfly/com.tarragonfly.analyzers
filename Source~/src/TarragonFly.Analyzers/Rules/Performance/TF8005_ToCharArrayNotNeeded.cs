using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8005: text.ToCharArray() only to loop over or index the characters, as in "foreach (char c in
    // line.ToCharArray())".
    //
    // A string can be looped over and indexed directly; ToCharArray copies every character into a new
    // array first, which is garbage in a dialogue printer that runs each frame.
    //
    // Only the overload without arguments counts, as the foreach collection or an indexed value.
    // Keeping the array or passing it on is fine. Reported on the ToCharArray name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ToCharArrayNotNeededAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8005",
            "ToCharArray only to loop over or index a string",
            "Use the string directly; ToCharArray() copies every character into a new array",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "ToCharArray" || invocation.ArgumentList.Arguments.Count != 0) return;
                bool looped = invocation.Parent is ForEachStatementSyntax loop && loop.Expression == invocation;
                bool indexed = invocation.Parent is ElementAccessExpressionSyntax element && element.Expression == invocation;
                if (!looped && !indexed) return;
                if (context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type?.SpecialType != SpecialType.System_String) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation()));
            }), SyntaxKind.InvocationExpression);
        }
    }
}