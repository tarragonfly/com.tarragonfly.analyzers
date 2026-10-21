using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1028: a warning switched off with nothing said about why: "#pragma warning disable TF8100"
    // on its own, "[SuppressMessage("Performance", "TF8100")]" without a Justification, or a bare
    // "[UsedImplicitly]".
    //
    // A suppression is a decision, and the next reader (or the agent cleaning up warnings) can't
    // tell a deliberate one from a shortcut. One line, "// the command table is built once at
    // startup", keeps it from being undone or copied blindly.
    //
    // A #pragma warning disable, with or without rule IDs, needs a comment on the same line or on
    // the line directly above; the same goes for [UsedImplicitly], where the comment says who calls
    // the member. [SuppressMessage] needs a Justification that isn't empty or blank. A doc comment
    // counts as a comment. Restore directives are never reported, and neither are attributes with
    // the same short name from another namespace. Reported on the directive or the attribute.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SuppressionWithoutReasonAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1028",
            "Suppression without a reason",
            "Say why this is suppressed in a comment",
            "Readability",
            CodeKind.All,
            description: "A #pragma warning disable or [UsedImplicitly] needs a comment on its line or the line above; [SuppressMessage] needs a Justification.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSemanticModelAction(context =>
            {
                SyntaxTree tree = context.SemanticModel.SyntaxTree;
                if (!scope.IsAnalyzed(tree)) return;
                SyntaxNode root = tree.GetRoot(context.CancellationToken);
                SourceText text = tree.GetText(context.CancellationToken);
                HashSet<int> commentLines = null;

                foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
                {
                    switch (node)
                    {
                        case PragmaWarningDirectiveTriviaSyntax pragma when pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword):
                            commentLines = commentLines ?? CommentLines(root, text);
                            if (!HasComment(commentLines, text, pragma.SpanStart)) context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(tree, DirectiveSpan(pragma))));
                            break;
                        case AttributeSyntax attribute:
                            ITypeSymbol attributeType = context.SemanticModel.GetTypeInfo(attribute, context.CancellationToken).Type;
                            if (SymbolNames.IsNamed(attributeType, "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute"))
                            {
                                if (!HasJustification(attribute, context.SemanticModel, context.CancellationToken)) context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation()));
                            }
                            else if (SymbolNames.IsNamed(attributeType, "JetBrains.Annotations.UsedImplicitlyAttribute"))
                            {
                                commentLines = commentLines ?? CommentLines(root, text);
                                if (!HasComment(commentLines, text, attribute.SpanStart)) context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation()));
                            }
                            break;
                    }
                }
            });
        }

        // "#pragma warning disable TF8100, TF8101": from the # to the last rule ID or the keyword,
        // without the trailing whitespace the directive node also covers.
        private static TextSpan DirectiveSpan(PragmaWarningDirectiveTriviaSyntax pragma)
        {
            int end = pragma.ErrorCodes.Count > 0 ? pragma.ErrorCodes.Last().Span.End : pragma.DisableOrRestoreKeyword.Span.End;
            return TextSpan.FromBounds(pragma.HashToken.SpanStart, end);
        }

        private static bool HasJustification(AttributeSyntax attribute, SemanticModel model, CancellationToken ct)
        {
            if (attribute.ArgumentList == null) return false;
            foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals?.Name.Identifier.ValueText != "Justification") continue;
                Optional<object> value = model.GetConstantValue(argument.Expression, ct);
                return value.HasValue && value.Value is string reason && !string.IsNullOrWhiteSpace(reason);
            }
            return false;
        }

        // A comment on the suppression's own line, before or after it, or one that ends on the line above.
        private static bool HasComment(HashSet<int> commentLines, SourceText text, int position)
        {
            int line = text.Lines.GetLineFromPosition(position).LineNumber;
            return commentLines.Contains(line) || commentLines.Contains(line - 1);
        }

        // The lines where a comment starts or ends, including comments inside directives, as in
        // "#pragma warning disable TF8100 // built once".
        private static HashSet<int> CommentLines(SyntaxNode root, SourceText text)
        {
            HashSet<int> lines = new HashSet<int>();
            foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (!IsComment(trivia)) continue;
                lines.Add(text.Lines.GetLineFromPosition(trivia.SpanStart).LineNumber);
                // A doc comment's span takes in its final line break, so its last character still
                // sits on the comment's last line.
                lines.Add(text.Lines.GetLineFromPosition(trivia.Span.End - 1).LineNumber);
            }
            return lines;
        }

        private static bool IsComment(SyntaxTrivia trivia)
        {
            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    return true;
                default:
                    return false;
            }
        }
    }
}
