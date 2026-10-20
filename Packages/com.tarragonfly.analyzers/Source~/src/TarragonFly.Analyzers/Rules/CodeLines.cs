using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules
{
    // Counts lines of code for the size rules: a line counts when any token sits on it, so blank lines,
    // comment-only lines and preprocessor lines don't.
    public static class CodeLines
    {
        // The tokens must come in source order, as DescendantTokens() gives them.
        public static int Count(IEnumerable<SyntaxToken> tokens, SourceText text)
        {
            int count = 0;
            int lastCounted = -1;
            foreach (SyntaxToken token in tokens)
            {
                if (token.Span.IsEmpty) continue;
                int first = text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
                int last = text.Lines.GetLineFromPosition(token.Span.End).LineNumber;
                if (first <= lastCounted) first = lastCounted + 1;
                if (last >= first) count += last - first + 1;
                if (last > lastCounted) lastCounted = last;
            }
            return count;
        }
    }
}