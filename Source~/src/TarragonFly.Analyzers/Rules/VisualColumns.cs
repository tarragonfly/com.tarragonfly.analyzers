using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules
{
    // Columns as an editor shows them, with tabs expanded, for rules that compare indentation.
    // Roslyn's line positions count a tab as one character, so a tab-indented body would look less
    // indented than a space-indented "} else" above it.
    public static class VisualColumns
    {
        public const int TabWidth = 4;

        public static int Of(SyntaxToken token)
        {
            SourceText text = token.SyntaxTree.GetText();
            TextLine line = text.Lines.GetLineFromPosition(token.SpanStart);
            int column = 0;
            for (int position = line.Start; position < token.SpanStart; position++)
            {
                column = text[position] == '\t' ? (column / TabWidth + 1) * TabWidth : column + 1;
            }
            return column;
        }

        public static int Of(SyntaxNode node)
        {
            return Of(node.GetFirstToken());
        }
    }
}