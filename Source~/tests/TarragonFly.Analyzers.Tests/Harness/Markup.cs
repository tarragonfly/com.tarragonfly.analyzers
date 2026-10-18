using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Tests.Harness
{
    // Expected diagnostics written inline: "void {|TF1000:Run|}()" expects TF1000 on "Run".
    public static class Markup
    {
        public static string Parse(string markup, List<(string Id, TextSpan Span)> expected)
        {
            StringBuilder text = new StringBuilder();
            Stack<(string Id, int Start)> open = new Stack<(string Id, int Start)>();
            int i = 0;
            while (i < markup.Length)
            {
                if (StartsWith(markup, i, "{|"))
                {
                    int colon = markup.IndexOf(':', i + 2);
                    if (colon < 0) throw new FormatException($"Missing ':' after '{{|' at {i}.");
                    open.Push((markup.Substring(i + 2, colon - i - 2), text.Length));
                    i = colon + 1;
                }
                else if (StartsWith(markup, i, "|}"))
                {
                    if (open.Count == 0) throw new FormatException($"'|}}' without '{{|' at {i}.");
                    (string id, int start) = open.Pop();
                    expected.Add((id, TextSpan.FromBounds(start, text.Length)));
                    i += 2;
                }
                else
                {
                    text.Append(markup[i]);
                    i++;
                }
            }
            if (open.Count > 0) throw new FormatException("A '{|' marker is not closed.");
            return text.ToString();
        }

        private static bool StartsWith(string text, int index, string value)
        {
            return string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
        }
    }
}