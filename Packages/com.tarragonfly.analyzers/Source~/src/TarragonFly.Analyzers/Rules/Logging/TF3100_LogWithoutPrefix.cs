using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Logging
{
    // TF3100: a Unity log message that doesn't start with a tag, as in
    // "Debug.LogWarning("Clip registry is not assigned.")" in AudioManager, where the project expects
    // "[AudioManager] Clip registry is not assigned.".
    //
    // A tag says where the line came from, so a Console or player log full of warnings can be
    // searched and filtered without opening each stack trace (player builds often have none).
    //
    // The tag's shape comes from the log_prefix setting, "[{Type}]" by default: the text before
    // {Type}, at least one character with no spaces, then the text after it. Any tag of that shape
    // counts, so a subsystem tag such as "[ScenarioEngine]" is fine too, and the warning suggests the
    // name of the type the call is in. A log_prefix without {Type} has to match exactly. Checked: the
    // message or format argument of Debug.Log, LogWarning, LogError, LogAssertion and their Format versions. The
    // start of the message is read from string literals, interpolated strings, + chains and constants;
    // a message the analyzer can't read, such as a variable passed in, is skipped. Reported on the
    // message. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LogWithoutPrefixAnalyzer : ScopedAnalyzer
    {
        private const string TypePlaceholder = "{Type}";

        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3100",
            "Log message without a tag",
            "Start the message with a tag such as '{0}' so the log shows where it came from",
            "Logging",
            CodeKind.Game);

        private static readonly HashSet<string> LogMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Log", "LogWarning", "LogError", "LogAssertion", "LogFormat", "LogWarningFormat", "LogErrorFormat", "LogAssertionFormat"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            string prefixFormat = scope.Settings.LogPrefix;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !LogMethods.Contains(access.Name.Identifier.ValueText)) return;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation call)) return;
                if (!SymbolNames.IsNamed(call.TargetMethod.ContainingType, "UnityEngine.Debug")) return;

                IArgumentOperation message = call.Arguments.FirstOrDefault(a => a.Parameter != null && (a.Parameter.Name == "message" || a.Parameter.Name == "format"));
                if (message == null || message.ArgumentKind != ArgumentKind.Explicit) return;
                (string start, bool complete)? text = LeadingText(context.SemanticModel, message.Value.Syntax, context.CancellationToken);
                if (text == null) return;

                if (StartsWithTag(text.Value.start, text.Value.complete, prefixFormat)) return;
                string suggested = SuggestedTag(prefixFormat, context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken));
                if (suggested != null) context.ReportDiagnostic(Diagnostic.Create(Rule, message.Value.Syntax.GetLocation(), suggested));
            }), SyntaxKind.InvocationExpression);
        }

        // A message whose start is only partly known passes while the known part could still begin a tag.
        private static bool StartsWithTag(string start, bool complete, string format)
        {
            int placeholder = format.IndexOf(TypePlaceholder, StringComparison.Ordinal);
            if (placeholder < 0) return start.StartsWith(format, StringComparison.Ordinal) || (!complete && format.StartsWith(start, StringComparison.Ordinal));

            string before = format.Substring(0, placeholder);
            string after = format.Substring(placeholder + TypePlaceholder.Length);
            if (start.Length < before.Length) return !complete && before.StartsWith(start, StringComparison.Ordinal);
            if (!start.StartsWith(before, StringComparison.Ordinal)) return false;
            for (int i = before.Length; i < start.Length; i++)
            {
                if (i > before.Length && string.CompareOrdinal(start, i, after, 0, after.Length) == 0) return true;
                if (char.IsWhiteSpace(start[i])) return false;
            }
            return after.Length == 0 ? start.Length > before.Length || !complete : !complete;
        }

        // The tag with the name of the type the call is in, or null outside a type.
        private static string SuggestedTag(string format, ISymbol enclosing)
        {
            if (!format.Contains(TypePlaceholder)) return format;
            INamedTypeSymbol type = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
            return type == null ? null : format.Replace(TypePlaceholder, type.Name);
        }

        // The text the message starts with, and whether that is the whole message; null when the
        // analyzer can't tell.
        private static (string, bool)? LeadingText(SemanticModel model, SyntaxNode expression, CancellationToken ct)
        {
            Optional<object> constant = model.GetConstantValue(expression, ct);
            if (constant.HasValue) return constant.Value is string value ? (value, true) : ((string, bool)?)null;

            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return LeadingText(model, parenthesized.Expression, ct);
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                    (string, bool)? left = LeadingText(model, binary.Left, ct);
                    if (left == null) return null;
                    return (left.Value.Item1, false);
                case InterpolatedStringExpressionSyntax interpolated:
                    return LeadingText(model, interpolated, ct);
                default:
                    return null;
            }
        }

        // Text parts and constant holes such as {nameof(AudioManager)}, up to the first hole whose value
        // is only known at run time.
        private static (string, bool)? LeadingText(SemanticModel model, InterpolatedStringExpressionSyntax interpolated, CancellationToken ct)
        {
            System.Text.StringBuilder start = new System.Text.StringBuilder();
            foreach (InterpolatedStringContentSyntax content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    start.Append(text.TextToken.ValueText);
                    continue;
                }
                InterpolationSyntax hole = (InterpolationSyntax)content;
                Optional<object> value = model.GetConstantValue(hole.Expression, ct);
                if (hole.AlignmentClause != null || hole.FormatClause != null || !value.HasValue || !(value.Value is string constant)) return (start.ToString(), false);
                start.Append(constant);
            }
            return (start.ToString(), true);
        }
    }
}