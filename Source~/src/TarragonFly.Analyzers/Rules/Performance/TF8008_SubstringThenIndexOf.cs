using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8008: IndexOf called on the result of Substring, as in "line.Substring(start).IndexOf(':')".
    //
    // Substring copies the rest of the text into a new string just to search it; IndexOf takes a
    // start index, line.IndexOf(':', start), and searches in place. Note the result then counts from
    // the start of the whole string.
    //
    // IndexOf, LastIndexOf, IndexOfAny and LastIndexOfAny count, straight after Substring. Reported on
    // the whole call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SubstringThenIndexOfAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8008",
            "Substring followed by IndexOf",
            "Pass a start index to {0} instead; Substring copies the text first",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "IndexOf" && name != "LastIndexOf" && name != "IndexOfAny" && name != "LastIndexOfAny") return;
                if (!(access.Expression is InvocationExpressionSyntax inner) || !(inner.Expression is MemberAccessExpressionSyntax innerAccess) || innerAccess.Name.Identifier.ValueText != "Substring") return;
                if (!(context.SemanticModel.GetSymbolInfo(inner, context.CancellationToken).Symbol is IMethodSymbol substring) || substring.ContainingType.SpecialType != SpecialType.System_String) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol search) || search.ContainingType.SpecialType != SpecialType.System_String) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}