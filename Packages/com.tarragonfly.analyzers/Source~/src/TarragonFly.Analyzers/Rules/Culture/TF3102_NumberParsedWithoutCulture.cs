using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3102: a fractional number parsed without saying which culture's format it is in, as in
    // "float.Parse(save.Speed)".
    //
    // Parse reads the text in the player's culture, so on a German PC "1.5" fails or comes out as 15
    // and saves or scenario values written elsewhere stop loading. Pass CultureInfo.InvariantCulture.
    //
    // Checked: Parse and TryParse on float, double and decimal, and Convert.ToSingle, ToDouble and
    // ToDecimal given a string, with no IFormatProvider argument or a null one. Integer types aren't
    // checked, since only the sign symbol changes with culture. Reported on the whole call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NumberParsedWithoutCultureAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3102",
            "Number parsed without a culture",
            "'{0}' reads the number in the player's culture, so \"1.5\" can fail or load as 15; pass CultureInfo.InvariantCulture",
            "Culture",
            CodeKind.All,
            sourceRule: "CA1305");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                string name = CultureCalls.CalledName(invocation)?.Identifier.ValueText;
                if (name != "Parse" && name != "TryParse" && name != "ToSingle" && name != "ToDouble" && name != "ToDecimal") return;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation)) return;
                if (!ParsesFractionalNumber(operation.TargetMethod) || CultureCalls.PassesFormatProvider(operation)) return;
                string call = operation.TargetMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "." + name;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), call));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool ParsesFractionalNumber(IMethodSymbol method)
        {
            switch (method.ContainingType.SpecialType)
            {
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return method.IsStatic && (method.Name == "Parse" || method.Name == "TryParse");
                default:
                    return SymbolNames.IsNamed(method.ContainingType, "System.Convert") && method.Parameters.Length > 0
                        && method.Parameters[0].Type.SpecialType == SpecialType.System_String;
            }
        }
    }
}