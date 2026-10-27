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
}", SnippetAssembly.WithSettings("max_cognitive_complexity = 3"));
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
    }
}
