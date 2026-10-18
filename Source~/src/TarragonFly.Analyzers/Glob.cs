using System;
using System.Collections.Generic;

namespace TarragonFly.Analyzers
{
    // Assembly name patterns from the settings file: "*" matches any run of characters, case is ignored.
    public static class Glob
    {
        public static bool Matches(string pattern, string value)
        {
            return Matches(pattern, 0, value, 0);
        }

        private static bool Matches(string pattern, int p, string value, int v)
        {
            while (p < pattern.Length)
            {
                if (pattern[p] == '*')
                {
                    while (p < pattern.Length && pattern[p] == '*') p++;
                    if (p == pattern.Length) return true;
                    for (int start = v; start <= value.Length; start++)
                    {
                        if (Matches(pattern, p, value, start)) return true;
                    }
                    return false;
                }
                if (v == value.Length || char.ToUpperInvariant(pattern[p]) != char.ToUpperInvariant(value[v])) return false;
                p++;
                v++;
            }
            return v == value.Length;
        }

        public static bool MatchesAny(IEnumerable<string> patterns, string value)
        {
            if (value == null) return false;
            foreach (string pattern in patterns)
            {
                if (Matches(pattern, value)) return true;
            }
            return false;
        }
    }
}