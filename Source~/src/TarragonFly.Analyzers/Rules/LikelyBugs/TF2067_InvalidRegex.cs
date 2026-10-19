using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2067: a constant regular expression that doesn't parse, as in new Regex("[0-9") or
    // Regex.IsMatch(name, "(enemy").
    //
    // The Regex throws ArgumentException when the line first runs, which for a name filter or text
    // parser may be well into a play session.
    //
    // The Regex constructor and the static IsMatch, Match, Matches, Replace and Split count; constant
    // options are applied. The pattern is checked by building a Regex from it. Reported on the pattern.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InvalidRegexAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2067",
            "Regular expression that doesn't parse",
            "This pattern throws when the Regex is built: {0}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ArgumentListSyntax arguments = context.Node is InvocationExpressionSyntax invocation ? invocation.ArgumentList : ((BaseObjectCreationExpressionSyntax)context.Node).ArgumentList;
                if (arguments == null) return;
                if (!(context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!SymbolNames.IsNamed(method.ContainingType, "System.Text.RegularExpressions.Regex")) return;
                if (method.MethodKind != MethodKind.Constructor && !(method.IsStatic && IsPatternMethod(method.Name))) return;

                ExpressionSyntax patternExpression = null;
                RegexOptions options = RegexOptions.None;
                for (int i = 0; i < arguments.Arguments.Count; i++)
                {
                    ArgumentSyntax argument = arguments.Arguments[i];
                    string parameterName = argument.NameColon != null ? argument.NameColon.Name.Identifier.ValueText
                        : i < method.Parameters.Length ? method.Parameters[i].Name : null;
                    if (parameterName == "pattern") patternExpression = argument.Expression;
                    if (parameterName != "options") continue;
                    Optional<object> optionsValue = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
                    if (optionsValue.HasValue && optionsValue.Value is int bits) options = (RegexOptions)bits;
                }
                if (patternExpression == null) return;
                Optional<object> pattern = context.SemanticModel.GetConstantValue(patternExpression, context.CancellationToken);
                if (!pattern.HasValue || !(pattern.Value is string text)) return;

                string error = ParseError(text, options & ~RegexOptions.Compiled);
                if (error != null) context.ReportDiagnostic(Diagnostic.Create(Rule, patternExpression.GetLocation(), error));
            }), SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        }

        private static bool IsPatternMethod(string name)
        {
            return name == "IsMatch" || name == "Match" || name == "Matches" || name == "Replace" || name == "Split";
        }

        private static string ParseError(string pattern, RegexOptions options)
        {
            try
            {
                _ = new Regex(pattern, options);
                return null;
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
        }
    }
}