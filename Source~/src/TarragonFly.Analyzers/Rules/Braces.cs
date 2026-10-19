using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TarragonFly.Analyzers.Rules
{
    public static class Braces
    {
        // True when only whitespace and line breaks sit between the braces. A comment, a preprocessor
        // directive or code switched off with #if all count as content.
        public static bool HaveOnlyWhitespaceBetween(SyntaxToken openBrace, SyntaxToken closeBrace)
        {
            if (openBrace.GetNextToken() != closeBrace) return false;
            return IsWhitespace(openBrace.TrailingTrivia) && IsWhitespace(closeBrace.LeadingTrivia);
        }

        private static bool IsWhitespace(SyntaxTriviaList trivia)
        {
            foreach (SyntaxTrivia item in trivia)
            {
                if (!item.IsKind(SyntaxKind.WhitespaceTrivia) && !item.IsKind(SyntaxKind.EndOfLineTrivia)) return false;
            }
            return true;
        }
    }
}