using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2004 self-assignment, TF2007 dropped object, TF2011 collection passed to itself, TF2012 lost
    // increment, TF2013 non-short-circuit logic, TF2019 ignored result, TF2020 ToString returning
    // null, TF2023 for loop that never runs, TF2031 ignored stream read, TF2033 IndexOf > 0, TF2041
    // second OrderBy, TF2042 delegate chain subtraction, TF2062 exception never thrown.
    public class StatementAndCallTests
    {
        [Test]
        public void ValueAssignedToItself_IsReported()
        {
            AnalyzerVerifier.Verify(new SelfAssignmentAnalyzer(), @"
class Target
{
    int health;
    int[] items = new int[1];
    int Health { get; set; }

    Target(int health)
    {
        {|TF2004:health|} = health;
        this.health = health;
    }

    void Check(int a, int b)
    {
        {|TF2004:Health|} = Health;
        {|TF2004:items[0]|} = items[0];
        ({|TF2004:a|}, b) = (a, a);
    }

    Target Copy(int health) => new Target(health) { Health = Health, items = items };
}

record Save(int Slot)
{
    Save Next(int Slot) => this with { Slot = Slot };
}");
        }

        [Test]
        public void ObjectCreatedAndDropped_IsReported_ExceptUnityObjects()
        {
            AnalyzerVerifier.Verify(new DroppedObjectAnalyzer(), @"
using System.Collections.Generic;
using UnityEngine;

class Spawner
{
    void Check()
    {
        {|TF2007:new Spawner()|};
        {|TF2007:new List<int>()|};
        new GameObject(""Spawned"");
        _ = new Spawner();
        Spawner kept = new Spawner();
    }
}");
        }

        [Test]
        public void SetOperationWithItself_IsReported()
        {
            AnalyzerVerifier.Verify(new CollectionPassedToItselfAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(HashSet<int> visited, HashSet<int> other, List<int> enemies)
    {
        {|TF2011:visited|}.UnionWith(visited);
        bool r1 = {|TF2011:visited|}.SetEquals(visited);
        IEnumerable<int> r2 = {|TF2011:enemies|}.Except(enemies);
        visited.UnionWith(other);
        enemies.AddRange(enemies);
        IEnumerable<int> r3 = enemies.Concat(enemies);
    }
}");
        }

        [Test]
        public void PostfixIncrementWhoseValueIsLost_IsReported()
        {
            AnalyzerVerifier.Verify(new LostIncrementAnalyzer(), @"
class Target
{
    int field;

    int Check(int count, int combo)
    {
        count = {|TF2012:count++|};
        field = field++;
        count++;
        return {|TF2012:combo++|};
    }

    int Field() => field++;
}");
        }

        [Test]
        public void NonShortCircuitBoolOperator_IsReported()
        {
            AnalyzerVerifier.Verify(new NonShortCircuitLogicAnalyzer(), @"
class Target
{
    bool IsAlive;

    void Check(Target target, bool a, bool b, int x)
    {
        bool r1 = target != null {|TF2013:&|} target.IsAlive;
        bool r2 = a && b;
        bool r3 = a ^ b;
        a &= b;
        int r4 = x & 1;
    }
}");
        }

        [Test]
        public void IgnoredResultOfCallWithoutSideEffects_IsReported()
        {
            AnalyzerVerifier.Verify(new IgnoredResultAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class Item
{
    public override string ToString() => ""item"";
}

class Target
{
    void Check(string name, List<int> enemies, object o, Item item, Vector3 v)
    {
        {|TF2019:name.Trim()|};
        {|TF2019:enemies.Where(e => e > 0)|};
        {|TF2019:o.ToString()|};
        {|TF2019:o.GetHashCode()|};
        {|TF2019:Math.Abs(-1)|};
        {|TF2019:Mathf.Clamp(1f, 0f, 2f)|};
        {|TF2019:Vector3.Distance(v, v)|};
        item.ToString();
        int.TryParse(name, out int parsed);
        v.Normalize();
        enemies.Remove(1);
        name = name.Trim();
    }
}");
        }

        [Test]
        public void IgnoredResult_InTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new IgnoredResultAnalyzer(), "class Target { void Check(string name) { name.Trim(); } }", SnippetAssembly.Tests());
        }

        [Test]
        public void ToStringReturningNull_IsReported()
        {
            AnalyzerVerifier.Verify(new ToStringReturnsNullAnalyzer(), @"
class Returns
{
    bool ready;

    public override string ToString()
    {
        if (ready)
        {
            {|TF2020:return null;|}
        }

        return ""ready"";
    }
}

class Body
{
    public override string ToString() {|TF2020:=> null|};
}

class Fine
{
    string name;

    public override string ToString() => name ?? null;

    public string ToString(string format) => format;
}");
        }

        [Test]
        public void ForLoopWhoseConditionStartsFalse_IsReported()
        {
            AnalyzerVerifier.Verify(new ForLoopNeverRunsAnalyzer(), @"
class Target
{
    void Check(int count)
    {
        for (int i = 10; {|TF2023:i < 5|}; i++) { }
        for (int i = 0; {|TF2023:i > 3|}; i--) { }
        for (int i = 0; i < 5; i++) { }
        for (int i = 0; i < count; i++) { }
        for (int i = count; i < 5; i++) { }
    }
}");
        }

        [Test]
        public void StreamReadCountIgnored_IsReported()
        {
            AnalyzerVerifier.Verify(new IgnoredStreamReadAnalyzer(), @"
using System.IO;
using System.Threading.Tasks;

class Target
{
    async Task Check(Stream stream, byte[] buffer)
    {
        {|TF2031:stream.Read(buffer, 0, buffer.Length)|};
        await {|TF2031:stream.ReadAsync(buffer, 0, buffer.Length)|};
        _ = stream.Read(buffer, 0, buffer.Length);
        int read = stream.Read(buffer, 0, buffer.Length);
    }
}");
        }

        [Test]
        public void IndexOfGreaterThanZero_IsReported()
        {
            AnalyzerVerifier.Verify(new IndexOfGreaterThanZeroAnalyzer(), @"
using System.Collections.Generic;

class Target
{
    void Check(string name, List<int> list, int[] items)
    {
        bool r1 = name.IndexOf(""Boss"") {|TF2033:> 0|};
        bool r2 = {|TF2033:0 <|} list.IndexOf(3);
        bool r3 = System.Array.IndexOf(items, 1) {|TF2033:> 0|};
        bool r4 = name.IndexOf(""Boss"") >= 0;
        bool r5 = name.IndexOf(""Boss"") > 1;
    }
}");
        }

        [Test]
        public void SecondOrderBy_IsReported()
        {
            AnalyzerVerifier.Verify(new SecondOrderByAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> enemies)
    {
        IEnumerable<int> r1 = enemies.OrderBy(e => e).{|TF2041:OrderBy|}(e => -e);
        IEnumerable<int> r2 = enemies.OrderBy(e => e).ThenBy(e => -e).{|TF2041:OrderByDescending|}(e => e);
        IEnumerable<int> r3 = enemies.OrderBy(e => e).ThenBy(e => -e);
        IEnumerable<int> r4 = enemies.OrderBy(e => e).Where(e => e > 0).OrderBy(e => -e);
        IEnumerable<int> r5 = from e in enemies orderby e {|TF2041:orderby|} -e select e;
        IEnumerable<int> r6 = from e in enemies orderby e, -e select e;
    }
}");
        }

        [Test]
        public void SubtractingADelegateBuiltOnTheSpot_IsReported()
        {
            AnalyzerVerifier.Verify(new DelegateChainSubtractionAnalyzer(), @"
using System;

class Target
{
    Action handlers;
    event Action Died;

    Action Make() => OnDied;

    void Check(Action a, Action b)
    {
        Action r1 = {|TF2042:handlers - (a + b)|};
        {|TF2042:handlers -= a + b|};
        {|TF2042:handlers -= () => Console.WriteLine()|};
        {|TF2042:handlers -= new Action(OnDied)|};
        {|TF2042:handlers -= Make()|};
        handlers -= a;
        handlers -= OnDied;
        handlers -= this.OnDied;
        Died -= () => Console.WriteLine();
        Died -= a + b;
        int r2 = 3 - (1 + 2);
    }

    void OnDied() { }
}");
        }

        [Test]
        public void ExceptionCreatedWithoutThrow_IsReported()
        {
            AnalyzerVerifier.Verify(new ExceptionNeverThrownAnalyzer(), @"
using System;

class Target
{
    void Check(bool broken)
    {
        if (broken) {|TF2062:new InvalidOperationException(""No save slot"")|};
        if (broken) throw new InvalidOperationException();
        Exception kept = new ArgumentException();
    }
}");
        }
    }
}