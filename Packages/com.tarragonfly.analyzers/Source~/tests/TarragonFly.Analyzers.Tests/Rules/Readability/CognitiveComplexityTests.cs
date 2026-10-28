using System.Linq;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1019 cognitive complexity. Each body is reported with the limit one below its score and not
    // at its score, which pins the score itself.
    public class CognitiveComplexityTests
    {
        private const string Template = @"
using System;
using System.Collections.Generic;

class Target
{
    int MARK(bool a, bool b, bool c, int y, List<int> list, string s)
    {
        BODY
        return y;
    }
}";

        [TestCase("if (a) y++; if (b) y--;", 2)]
        [TestCase("if (a) { if (b) { if (c) y++; } }", 6)]
        [TestCase("if (a) y++; else if (b) y--; else y = 0;", 3)]
        [TestCase("if (a) { if (b) y++; else if (c) y--; else y = 0; }", 5)]
        [TestCase("if (a) y++; else { if (b) y--; }", 4)]
        [TestCase("if (a && b && c) y++;", 2)]
        [TestCase("if (a && b || c) y++;", 3)]
        [TestCase("if (a && (b || c)) y++;", 3)]
        [TestCase("if (a && !(b && c)) y++;", 3)]
        [TestCase("if ((a && b) && c) y++;", 2)]
        [TestCase("if (a || b && c || a) y++;", 4)]
        [TestCase("if (a && b && !c && (a || (a && b) || (b && c))) y++;", 6)]
        [TestCase("if (a && (b || c) && a) y++;", 4)]
        [TestCase("if (a && (b && c)) y++;", 2)]
        [TestCase("if (a && !(b && c) && a) y++;", 3)]
        [TestCase("if (a && b && c || a || b && c) y++;", 4)]
        [TestCase("bool r = (a && b) == (b && c);", 2)]
        [TestCase("bool r = a || b && c || a;", 3)]
        [TestCase("bool r = a || b; if (c) y++;", 2)]
        [TestCase("y = a ? 1 : b ? 2 : 3;", 3)]
        [TestCase("y = (a ? b : c) ? 1 : 0;", 3)]
        [TestCase("y = a ? (b ? 1 : 2) : 3;", 3)]
        [TestCase("if (a) y = (a ? b : c) ? 1 : 0;", 6)]
        [TestCase("switch (y) { case 1: if (a) y++; break; }", 3)]
        [TestCase("y = y switch { 1 => a ? 1 : 2, _ => 3 };", 3)]
        [TestCase("foreach (int v in list) { while (a) { y++; } }", 3)]
        [TestCase("for (int i = 0; i < y; i++) { do { y--; } while (b); }", 3)]
        [TestCase("try { y++; } catch (Exception) when (a && b) { if (c) y--; } finally { y++; }", 4)]
        [TestCase("Action f = () => { if (a) y++; };", 2)]
        [TestCase("void Local() { if (a) y++; } Local();", 2)]
        [TestCase("goto done; done: y++; if (a) y--;", 2)]
        [TestCase("if (y > 9) y = MARK(a, b, c, y - 1, list, s);", 2)]
        [TestCase("y = y < 2 ? y : MARK(a, b, c, y - 1, list, s) + MARK(a, b, c, y - 2, list, s);", 2)]
        [TestCase("int Local(int n) => n < 2 ? n : Local(n - 1) + Local(n - 2); y = Local(y);", 3)]
        [TestCase("int Local(int n) { if (n < 2) return n; return Local(n - 1) + Local(n - 2); } y = Local(y);", 3)]
        [TestCase("MARK(a, b, c, y, list, s); void Local() { Local(); Local(); } MARK(a, b, c, y, list, s);", 2)]
        [TestCase("void Local() { void Inner() { Inner(); Inner(); } Local(); Local(); } MARK(a, b, c, y, list, s);", 3)]
        [TestCase("void Local() { MARK(a, b, c, y, list, s); } Local(); if (a) y++; if (b) y--;", 2)]
        [TestCase("if (s is not null && y is > 1 and < 5) y++;", 3)]
        [TestCase("if (y is 1 or > 2 and < 5 or 9) y++;", 4)]
        [TestCase("if (y is > 0 and (< 5 or > 9) and < 20) y++;", 4)]
        [TestCase("if (y is > 0 and (> 1 and < 5)) y++;", 2)]
        [TestCase("if (y is > 0 and not (> 1 and < 5) and < 20) y++;", 3)]
        [TestCase("foreach (int v in list) { if (v > 1) break; continue; }", 3)]
        public void Score_IsReportedAboveTheLimit(string body, int score)
        {
            string code = Template.Replace("BODY", body);
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), code.Replace("int MARK(", "int {|TF1019:Run|}(").Replace("MARK(", "Run("),
                SnippetAssembly.WithSettings("max_cognitive_complexity = " + (score - 1)));
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), code.Replace("MARK", "Run"),
                SnippetAssembly.WithSettings("max_cognitive_complexity = " + score));
        }

        [Test]
        public void ShorthandOperators_AddNothing()
        {
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), Template.Replace("MARK", "Run").Replace("BODY", "if (a) y++; s = s ?? \"x\"; y += s?.Length ?? 0;"),
                SnippetAssembly.WithSettings("max_cognitive_complexity = 1"));
        }

        [Test]
        public void EachMemberKind_IsReportedOnItsName()
        {
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), @"
class Target
{
    bool a;
    int y;

    Target() { if (a && y > 0) y++; }
    Target(int start) => y = a && start > 0 ? start : 0;

    int Property
    {
        {|TF1019:get|} { if (a && y > 0) y++; return y; }
        {|TF1019:set|} { if (a || value > 0) y = value; }
    }

    int Arrow => a && y > 0 ? 1 : 2;

    public static Target operator {|TF1019:+|}(Target left, Target right) => left.a && right.a ? left : right;

    void {|TF1019:Method|}() { if (a && y > 0) y++; }
    void Flat() { if (a) y++; }
}".Replace("Target() {", "{|TF1019:Target|}() {").Replace("Target(int start)", "{|TF1019:Target|}(int start)").Replace("int Arrow", "int {|TF1019:Arrow|}"),
                SnippetAssembly.WithSettings("max_cognitive_complexity = 1"));
        }

        [Test]
        public void FieldInitializers_AreScoredTogetherOnTheFirstVariable()
        {
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), @"
using System;

class Target
{
    static bool a;
    static Action {|TF1019:check|} = () => { if (a) { } }, other = () => { if (a) { } };
    static Action flat = null;
    static Action Property { get; } = () => { if (a) { } if (a) { } };
}", SnippetAssembly.WithSettings("max_cognitive_complexity = 1"));
        }

        [Test]
        public void FieldLambda_IsScoredLikeAMethodBody()
        {
            Assert.That(AnalyzerVerifier.Messages(new CognitiveComplexityAnalyzer(), @"
using System;

class Target
{
    static bool a;
    static Action check = () => { if (a) { if (a) { } } Action inner = () => { if (a) { } }; };
}", SnippetAssembly.WithSettings("max_cognitive_complexity = 1")),
                Is.EqualTo(new[] { "'check' has a cognitive complexity of 5, above the 1 allowed; split it or flatten its nesting" }));
        }

        [Test]
        public void DefaultLimit_Is15()
        {
            string fifteen = string.Concat(Enumerable.Repeat("if (a) y++; ", 15));
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), Template.Replace("MARK", "Run").Replace("BODY", fifteen));
            AnalyzerVerifier.Verify(new CognitiveComplexityAnalyzer(), Template.Replace("int MARK(", "int {|TF1019:Run|}(").Replace("BODY", fifteen + "if (a) y++;"));
        }

        [Test]
        public void Message_GivesTheNameScoreAndLimit()
        {
            Assert.That(AnalyzerVerifier.Messages(new CognitiveComplexityAnalyzer(), Template.Replace("MARK", "Run").Replace("BODY", "if (a) { if (b) { if (c) y++; } }"),
                    SnippetAssembly.WithSettings("max_cognitive_complexity = 5")),
                Is.EqualTo(new[] { "'Run' has a cognitive complexity of 6, above the 5 allowed; split it or flatten its nesting" }));
        }

        // Worked examples from the Cognitive Complexity whitepaper (v1.7, August 2023), ported to C#
        // with the paper's increments on each line. GetWords scores 1, which no limit reports, so it's
        // missing from the expected list.
        private const string WhitepaperExamples = @"
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

class Entry { public long Version; public Entry Previous; }
class RetryException : Exception { public long Version; public RetryException(long version) { Version = version; } }
class Symbol { public static readonly Symbol Unknown = new Symbol(); public bool IsMethod; public bool IsStatic; }

class Target
{
    const string SpecialChars = ""$^+.()[]{}|"";
    const long TimedOut = -1, Aborted = -2;
    Entry first;
    bool saveEvent;

    // 'An illustration of the problem'. C# has no labeled continue; continue OUT becomes a goto.
    int SumOfPrimes(int max)
    {
        int total = 0;
        for (int i = 1; i <= max; ++i)                  // +1
        {
            for (int j = 2; j < i; ++j)                 // +2 (nesting = 1)
            {
                if (i % j == 0)                         // +3 (nesting = 2)
                {
                    goto next;                          // +1
                }
            }
            total += i;
        next:;
        }
        return total;
    }                                                   // 7

    string GetWords(int number)
    {
        switch (number)                                 // +1
        {
            case 1: return ""one"";
            case 2: return ""a couple"";
            case 3: return ""a few"";
            default: return ""lots"";
        }
    }                                                   // 1

    // 'Increment for nested flow-break structures'.
    void MyMethod(bool condition1, bool condition2)
    {
        try
        {
            if (condition1)                             // +1
            {
                for (int i = 0; i < 10; i++)            // +2 (nesting = 1)
                {
                    while (condition2) { }              // +3 (nesting = 2)
                }
            }
        }
        catch (Exception)                               // +1
        {
            if (condition2) { }                         // +2 (nesting = 1)
        }
    }                                                   // 9

    void MyMethod2(bool condition1)
    {
        Action r = () =>                                // +0 (nesting is now 1)
        {
            if (condition1) { }                         // +2 (nesting = 1)
        };
    }                                                   // 2

    // 'Sequences of logical operators'.
    bool MixedOperators(bool a, bool b, bool c, bool d, bool e, bool f)
    {
        if (a                                           // +1 for if
            && b && c                                   // +1
            || d || e                                   // +1
            && f)                                       // +1
            return true;
        return false;
    }                                                   // 4

    bool NegatedSequence(bool a, bool b, bool c)
    {
        if (a                                           // +1 for if
            &&                                          // +1
            !(b && c))                                  // +1
            return true;
        return false;
    }                                                   // 3

    // Appendix C, JavaSymbol.overriddenSymbolFrom.
    Symbol OverriddenSymbolFrom(List<Symbol> classType)
    {
        if (classType == null)                          // +1
        {
            return Symbol.Unknown;
        }
        bool unknownFound = false;
        foreach (Symbol overrideSymbol in classType)    // +1
        {
            if (overrideSymbol.IsMethod                 // +2 (nesting = 1)
                && !overrideSymbol.IsStatic)            // +1
            {
                if (CanOverride(overrideSymbol))        // +3 (nesting = 2)
                {
                    bool? overriding = CheckOverridingParameters(overrideSymbol);
                    if (overriding == null)             // +4 (nesting = 3)
                    {
                        if (!unknownFound)              // +5 (nesting = 4)
                        {
                            unknownFound = true;
                        }
                    }
                    else if (overriding.Value)          // +1
                    {
                        return overrideSymbol;
                    }
                }
            }
        }
        if (unknownFound)                               // +1
        {
            return Symbol.Unknown;
        }
        return null;
    }                                                   // 19

    bool CanOverride(Symbol symbol) => true;
    bool? CheckOverridingParameters(Symbol symbol) => null;

    // Appendix C, TimelyResource.addVersion. lock stands in for synchronized.
    void AddVersion(Entry entry, bool active, Func<long, long> dependency)
    {
        while (true)                                    // +1
        {
            try
            {
                lock (this)
                {
                    if (first != null)                  // +2 (nesting = 1)
                    {
                        if (first.Version > entry.Version)                  // +3 (nesting = 2)
                        {
                            throw new InvalidOperationException();
                        }
                        if (active)                     // +3 (nesting = 2)
                        {
                            for (Entry e = first; e != null; e = e.Previous) // +4 (nesting = 3)
                            {
                                long depends = dependency(e.Version);
                                if (depends == TimedOut)                    // +5 (nesting = 4)
                                {
                                    throw new RetryException(e.Version);
                                }
                                if (depends != 0                            // +5 (nesting = 4)
                                    && depends != Aborted)                  // +1
                                {
                                    throw new InvalidOperationException();
                                }
                            }
                        }
                    }
                    entry.Previous = first;
                    first = entry;
                    break;
                }
            }
            catch (RetryException retry)                // +2 (nesting = 1)
            {
                try
                {
                    long depends = dependency(retry.Version);
                    if (depends != 0                    // +3 (nesting = 2)
                        && depends != Aborted)          // +1
                    {
                        throw new InvalidOperationException();
                    }
                }
                catch (ThreadInterruptedException interrupted)              // +3 (nesting = 2)
                {
                    throw new OperationCanceledException(null, interrupted);
                }
            }
            catch (ThreadInterruptedException interrupted)                  // +2 (nesting = 1)
            {
                throw new OperationCanceledException(null, interrupted);
            }
        }
    }                                                   // 35

    // Appendix C, WildcardPattern.toRegexp.
    static string ToRegexp(string antPattern, string directorySeparator)
    {
        string escapedDirectorySeparator = ""\\"" + directorySeparator;
        StringBuilder sb = new StringBuilder(antPattern.Length);
        sb.Append('^');
        int i = antPattern.StartsWith(""/"") ||         // +1
            antPattern.StartsWith(""\\"") ? 1 : 0;      // +1
        while (i < antPattern.Length)                   // +1
        {
            char ch = antPattern[i];
            if (SpecialChars.IndexOf(ch) != -1)         // +2 (nesting = 1)
            {
                sb.Append('\\').Append(ch);
            }
            else if (ch == '*')                         // +1
            {
                if (i + 1 < antPattern.Length           // +3 (nesting = 2)
                    && antPattern[i + 1] == '*')        // +1
                {
                    if (i + 2 < antPattern.Length       // +4 (nesting = 3)
                        && IsSlash(antPattern[i + 2]))  // +1
                    {
                        sb.Append(""(?:.*"").Append(escapedDirectorySeparator).Append(""|)"");
                        i += 2;
                    }
                    else                                // +1
                    {
                        sb.Append("".*"");
                        i += 1;
                    }
                }
                else                                    // +1
                {
                    sb.Append(""[^"").Append(escapedDirectorySeparator).Append(""]*?"");
                }
            }
            else if (ch == '?')                         // +1
            {
                sb.Append(""[^"").Append(escapedDirectorySeparator).Append(""]"");
            }
            else if (IsSlash(ch))                       // +1
            {
                sb.Append(escapedDirectorySeparator);
            }
            else                                        // +1
            {
                sb.Append(ch);
            }
            i++;
        }
        sb.Append('$');
        return sb.ToString();
    }                                                   // 20

    static bool IsSlash(char c) => c == '/' || c == '\\';

    // Appendix C, YUI model.js save. The JS idioms 'x || (x = {})' and 'cb && cb()' keep their || and &&.
    Target Save(object options, Func<object, bool> callback)
    {
        Target self = this;
        if (options is Func<object, bool> function)     // +1
        {
            callback = function;
            options = new object();
        }
        _ = options != null || (options = new object()) != null;            // +1
        Validate(error =>
        {
            if (error != null)                          // +2 (nesting = 1)
            {
                _ = callback != null && callback(error);                    // +1
                return;
            }
            Sync(IsNew() ? ""create"" : ""update"", (syncError, response) => // +2 (nesting = 1)
            {
                if (syncError != null)                  // +3 (nesting = 2)
                {
                    Fire(""error"");
                }
                else                                    // +1
                {
                    if (!saveEvent)                     // +4 (nesting = 3)
                    {
                        saveEvent = true;
                    }
                    if (response != null)               // +4 (nesting = 3)
                    {
                        options = response;
                    }
                    Fire(""save"");
                }
                _ = callback != null && callback(response);                 // +1
            });
        });
        return self;
    }                                                   // 20

    void Validate(Action<object> done) { }
    bool IsNew() => false;
    void Sync(string mode, Action<object, object> done) { }
    void Fire(string name) { }
}";

        [Test]
        public void WhitepaperExamples_ScoreAsInThePaper()
        {
            (string Name, int Score)[] expected =
            {
                ("SumOfPrimes", 7), ("MyMethod", 9), ("MyMethod2", 2), ("MixedOperators", 4), ("NegatedSequence", 3),
                ("OverriddenSymbolFrom", 19), ("AddVersion", 35), ("ToRegexp", 20), ("Save", 20),
            };
            Assert.That(AnalyzerVerifier.Messages(new CognitiveComplexityAnalyzer(), WhitepaperExamples, SnippetAssembly.WithSettings("max_cognitive_complexity = 1")),
                Is.EqualTo(expected.Select(e => $"'{e.Name}' has a cognitive complexity of {e.Score}, above the 1 allowed; split it or flatten its nesting")));
        }
    }
}
