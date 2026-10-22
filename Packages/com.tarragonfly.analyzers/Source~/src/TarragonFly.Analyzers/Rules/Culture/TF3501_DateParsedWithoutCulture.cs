using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3501: a date or time parsed without saying which culture's format it is in, as in
    // "DateTime.Parse(save.LastPlayed)".
    //
    // Parse reads the text in the player's culture, so "03/04/2025" is March on one PC and April on
    // another, and a save written on one machine can fail to load on the next. Pass
    // CultureInfo.InvariantCulture, or use ParseExact with a fixed format.
    //
    // Checked: Parse, TryParse, ParseExact and TryParseExact on DateTime, DateTimeOffset and TimeSpan,
    // with no IFormatProvider argument or a null one. Reported on the whole call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DateParsedWithoutCultureAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3501",
            "Date or time parsed without a culture",
            "'{0}.{1}' reads the text in the player's culture; pass CultureInfo.InvariantCulture",
            "Culture",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "Parse" && name != "TryParse" && name != "ParseExact" && name != "TryParseExact") return;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation)) return;
                INamedTypeSymbol type = operation.TargetMethod.ContainingType;
                if (!SymbolNames.IsNamed(type, "System.DateTime") && !SymbolNames.IsNamed(type, "System.DateTimeOffset") && !SymbolNames.IsNamed(type, "System.TimeSpan")) return;
                if (CultureArguments.PassesFormatProvider(operation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), type.Name, name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}