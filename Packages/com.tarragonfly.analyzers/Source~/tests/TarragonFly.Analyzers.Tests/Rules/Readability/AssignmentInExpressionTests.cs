using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class AssignmentInExpressionTests
    {
        private static void Verify(string statements)
        {
            AnalyzerVerifier.Verify(new AssignmentInExpressionAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.IO;

class Inventory
{
    int count;
    int other;
    bool full;
    List<int> cache;
    int[] slots = new int[3];

    void Use(object value) { }

    class Box
    {
        public int Value;
    }

    int Run(int x, bool b, TextReader reader)
    {
" + statements + @"
        return 0;
    }
}");
        }

        [Test]
        public void AssignmentsInsideExpressions_AreReportedOnTheOperator()
        {
            Verify(@"
        Use(count {|TF1009:=|} 3);
        Use(count {|TF1009:+=|} 3);
        if (b && (full {|TF1009:=|} x > 3)) { }
        if ((full {|TF1009:=|} b)) { }
        bool r = (count {|TF1009:=|} x) > 0;
        int y = (count {|TF1009:=|} x) + 1;
        slots[count {|TF1009:=|} 1] = 2;
        int z = b ? (count {|TF1009:=|} 1) : 2;
        Func<int> f = () => (count {|TF1009:=|} 3) + 1;");
        }

        [Test]
        public void StatementsChainsInitializersAndReturns_AreFine()
        {
            Verify(@"
        count = x;
        count = other = x;
        int y = count = x;
        int z = (count += 2);
        Action set = () => count = 5;
        List<int> list = cache ?? (cache = new List<int>());
        List<int> list2 = cache ??= new List<int>();
        for (int i = 0, j = 0; i < 10; i = i + 1, j = j + 2) { }
        Box box = new Box { Value = x };
        if (b) return count = x;");
        }

        [Test]
        public void ReadAndCompareInAConditionIdiom_IsFine()
        {
            Verify(@"
        string line;
        while ((line = reader.ReadLine()) != null) { }
        if ((count = x) > 0 && b) { }
        do { x--; } while ((count = x) > 0);
        for (int i = 0; (count = i) < 3; i++) { }");
        }

        [Test]
        public void ComparisonOutsideAConditionIdiom_IsReported()
        {
            Verify(@"
        bool r = (count {|TF1009:=|} x) == 1;
        Use((count {|TF1009:=|} x) == 1);");
        }

        [Test]
        public void LazyInitPatternConditionsAndStatementValues_AreFine()
        {
            Verify(@"
        (cache ??= new List<int>()).Add(x);
        Use(cache ??= new List<int>());
        bool empty = (cache ??= new List<int>()).Count == 0;
        while ((count = x--) is not -1) { }
        if ((count = x) is > 3 && b) { }
        object o;
        if ((o = reader.ReadLine()) is string text) { }
        switch (count = x) { case 1: break; }
        lock (cache = new List<int>()) { }
        TextReader held;
        using (held = new StringReader("""")) { }");
        }

        [Test]
        public void PatternOutsideAConditionSwitchExpressionAndCoalesceSettingAnotherVariable_AreReported()
        {
            Verify(@"
        bool r = (count {|TF1009:=|} x) is 3;
        int s = (count {|TF1009:=|} x) switch { 1 => 2, _ => 3 };
        List<int> spare = null;
        Use(cache ?? (spare {|TF1009:=|} new List<int>()));
        Use(cache ?? (this.cache {|TF1009:=|} new List<int>()));");
        }

        [Test]
        public void YieldReturnedAssignment_IsFine()
        {
            AnalyzerVerifier.Verify(new AssignmentInExpressionAnalyzer(), @"
using System.Collections;

class Spawner
{
    object routine;
    int count;

    IEnumerator Run()
    {
        yield return routine = new object();
        yield return (routine = new object());
        yield return count += 1;
    }
}");
        }
    }
}