using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8016: StartsWith or EndsWith with a one-character string, as in "path.EndsWith("/")".
    //
    // The string overload compares with the current culture, which is slow and can give surprising
    // results; the char overload, path.EndsWith('/'), is an ordinal check of one character.
    //
    // Only a string literal one character long, with no other arguments, counts; named constants aren't
    // checked. Reported on the method name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SingleCharStringPrefixAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8016",
            "String overload of StartsWith or EndsWith for one character",
            "Use the char overload of {0}; it is a fast ordinal check instead of a culture-aware string compare",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!PassesOneCharLiteral(invocation)) return;
                MemberAccessExpressionSyntax access = (MemberAccessExpressionSyntax)invocation.Expression;
                string name = access.Name.Identifier.ValueText;
                if (name != "StartsWith" && name != "EndsWith") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || method.ContainingType.SpecialType != SpecialType.System_String) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation(), name));
            }), SyntaxKind.InvocationExpression);
        }

        // "x.M("a")": a member call whose only argument is a one-character string literal. TF3104 uses
        // it to leave these StartsWith and EndsWith calls to this rule, whose char fix clears both.
        internal static bool PassesOneCharLiteral(InvocationExpressionSyntax invocation)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax) || invocation.ArgumentList.Arguments.Count != 1) return false;
            return invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && literal.Token.ValueText.Length == 1;
        }
    }
}