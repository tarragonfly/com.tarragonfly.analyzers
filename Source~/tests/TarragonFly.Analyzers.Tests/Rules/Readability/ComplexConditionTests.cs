using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class ComplexConditionTests
    {
        private static void Verify(string statements, SnippetAssembly assembly = null)
        {
            AnalyzerVerifier.Verify(new ComplexConditionAnalyzer(), @"
using System;
using System.Linq;

class Player
{
    bool Check(bool value) => value;

    object Run(bool a, bool b, bool c, bool d, bool e, object o, int[] items)
    {
" + statements + @"
        return null;
    }
}", assembly);
        }

        [Test]
        public void FourOperators_AreReportedOnTheWholeExpression()
        {
            Verify(@"
        if ({|TF1007:a && b || c && d && e|}) { }
        bool f = {|TF1007:a ? b && c : d || e && a|};
        int g = {|TF1007:(a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) + (d ? 1 : 0)|};
        bool h = {|TF1007:!(a && b) && !(c && d) && e|};");
        }

        [Test]
        public void ThreeOperators_AreFine()
        {
            Verify(@"
        if (a && b && c && d) { }
        bool f = a ? b && c : d;");
        }

        [Test]
        public void ArgumentsLambdasCastsAndAssignments_StartTheirOwnCount()
        {
            Verify(@"
        bool f = a && b && Check(c && d && a);
        bool g = a && b && items.Any(i => c && d && i > 0);
        Func<bool> h = () => {|TF1007:a && b && c && d && e|};
        bool i = (bool)(object){|TF1007:(a && b && c && d && e)|};
        bool j;
        j = {|TF1007:a && b && c && d && e|};");
        }

        [Test]
        public void CoalesceBitwiseAndPatternOperators_DontCount()
        {
            Verify(@"
        object f = o ?? o ?? o ?? o ?? o;
        bool g = a & b & c & d & e;
        bool h = items.Length is > 0 and < 10 or 20 or 30 && a;");
        }

        [Test]
        public void EqualsImplementations_AreSkipped()
        {
            AnalyzerVerifier.Verify(new ComplexConditionAnalyzer(), @"
using System;

class Cell : IEquatable<Cell>
{
    int x, y, z, w;

    public bool Equals(Cell other) => other != null && x == other.x && y == other.y && z == other.z && w == other.w;

    public override bool Equals(object obj) => obj is Cell other && x == other.x && y == other.y && z == other.z && w == other.w;

    public override int GetHashCode() => {|TF1007:x > 0 && y > 0 && z > 0 && w > 0 ? 1 : 0|};
}");
        }

        [Test]
        public void PreprocessorConditions_AreChecked()
        {
            AnalyzerVerifier.Verify(new ComplexConditionAnalyzer(), @"#if {|TF1007:DEBUG || TRACE || UNITY_EDITOR || UNITY_IOS || UNITY_ANDROID|}
#endif
class Player { }");
        }

        [Test]
        public void Limit_ComesFromSettings()
        {
            Verify(@"
        if ({|TF1007:a && b || c|}) { }
        if (a && b) { }", SnippetAssembly.WithSettings("max_condition_operators = 1"));
        }
    }
}