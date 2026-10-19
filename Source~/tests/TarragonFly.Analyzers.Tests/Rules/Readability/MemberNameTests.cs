using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1008 locals hiding members, TF1017 nested members hiding outer statics, TF1020 global types.
    public class MemberNameTests
    {
        [Test]
        public void LocalWithAMemberName_IsReported()
        {
            AnalyzerVerifier.Verify(new LocalHidesFieldAnalyzer(), @"
using System;

class Player
{
    int health;
    static int count;
    public int Armor { get; set; }
    event Action changed;

    Player()
    {
        int {|TF1008:count|} = 2;
    }

    void Hit(int[] items, object o)
    {
        int {|TF1008:health|} = 5;
        int armor = 1;
        foreach (int {|TF1008:Armor|} in items) { }
        if (o is int {|TF1008:changed|}) { }
        bool ok = int.TryParse(""1"", out int {|TF1008:count|});
    }

    void Inner()
    {
        try { } catch (Exception {|TF1008:health|}) { }
    }
}");
        }

        [Test]
        public void ParametersStaticMethodsAndInheritedMembers_AreFine()
        {
            AnalyzerVerifier.Verify(new LocalHidesFieldAnalyzer(), @"
using System;

class Unit
{
    protected int speed;
}

class Player : Unit
{
    int health;

    static void Reset()
    {
        int health = 0;
    }

    void Hit()
    {
        int speed = 1;
        Func<int, int> f = health => health + 1;
        int Local(int health) => health;
    }
}");
        }

        [Test]
        public void NestedMemberWithAnOuterStaticName_IsReported()
        {
            AnalyzerVerifier.Verify(new NestedMemberHidesOuterStaticAnalyzer(), @"
class Outer
{
    public static int Count;
    public static void Reset() { }
    public static int Total { get; set; }
    public int Instance;

    class Inner
    {
        public int {|TF1017:Count|};
        public void {|TF1017:Reset|}(int amount) { }
        public int {|TF1017:Total|} { get; set; }
        public int Instance;
    }

    interface INested
    {
        void {|TF1017:Reset|}();
    }

    enum Kind
    {
        Count
    }
}");
        }

        [Test]
        public void TypeOutsideANamespace_IsReported()
        {
            AnalyzerVerifier.Verify(new TypeInGlobalNamespaceAnalyzer(), @"
class {|TF1020:Player|}
{
    class Nested { }
}

delegate void {|TF1020:Spawned|}();

enum {|TF1020:Team|} { Red }

namespace Game
{
    class Enemy { }
}");
        }
    }
}