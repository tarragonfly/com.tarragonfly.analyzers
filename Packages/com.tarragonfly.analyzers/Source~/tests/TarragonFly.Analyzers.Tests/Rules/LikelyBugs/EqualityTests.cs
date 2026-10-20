using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2001 Equals without GetHashCode, TF2006 identical operands, TF2026 GetHashCode over mutable fields.
    public class EqualityTests
    {
        [Test]
        public void HalfOfTheEqualsPair_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new EqualsWithoutGetHashCodeAnalyzer(), @"
class {|TF2001:OnlyEquals|}
{
    public override bool Equals(object obj) => obj is OnlyEquals;
}

struct {|TF2001:OnlyHash|}
{
    public override int GetHashCode() => 1;
}

class Both
{
    public override bool Equals(object obj) => obj is Both;
    public override int GetHashCode() => 1;
}

class {|TF2001:Derived|} : Both
{
    public override bool Equals(object obj) => obj is Derived;
}

class Typed
{
    public bool Equals(Typed other) => other != null;
}

record Point(int X);");
        }

        [Test]
        public void HalfOfTheEqualsPair_InTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new EqualsWithoutGetHashCodeAnalyzer(), "class OnlyEquals { public override bool Equals(object obj) => true; }", SnippetAssembly.Tests());
        }

        [Test]
        public void SameExpressionOnBothSides_IsReported()
        {
            AnalyzerVerifier.Verify(new IdenticalOperandsAnalyzer(), @"
class Target
{
    int x;

    static int Next() => 1;

    void Check(int a, bool c, object o, int[] items, Target other)
    {
        bool r1 = a == {|TF2006:a|};
        bool r2 = c && {|TF2006:c|};
        int r3 = a - {|TF2006:a|};
        int r4 = a % {|TF2006:a|};
        bool r5 = items[0] < {|TF2006:items[0]|};
        bool r6 = Next() == {|TF2006:Next()|};
        bool r7 = other.x == {|TF2006:other.x|};
        bool r8 = {|TF2006:o|}.Equals(o);
        bool r9 = Equals({|TF2006:a|}, a);
    }
}");
        }

        [Test]
        public void DoublingShiftsNaNChecksAndDifferentSides_AreFine()
        {
            AnalyzerVerifier.Verify(new IdenticalOperandsAnalyzer(), @"
class Target
{
    int x;

    void Check(int a, int b, float f, double d)
    {
        int r1 = a + a;
        int r2 = a * a;
        int r3 = a << a;
        bool r4 = f != f;
        bool r5 = d == d;
        bool r6 = this.x == x;
        bool r7 = a == b;
    }
}");
        }

        [Test]
        public void GetHashCodeOverAMutableField_IsReportedOnce()
        {
            AnalyzerVerifier.Verify(new HashCodeReadsMutableFieldAnalyzer(), @"
class Cell
{
    int x;
    int y;
    readonly int z = 3;

    public override bool Equals(object obj) => obj is Cell;
    public override int {|TF2026:GetHashCode|}() => x * 31 + y + z;
}");
        }

        [Test]
        public void GetHashCodeOverReadOnlyStateOrInAStruct_IsFine()
        {
            AnalyzerVerifier.Verify(new HashCodeReadsMutableFieldAnalyzer(), @"
class Cell
{
    readonly int x;
    const int Seed = 7;
    static int shared;
    public int Property { get; set; }

    public override bool Equals(object obj) => obj is Cell;
    public override int GetHashCode() => x + Seed + shared + Property;
}

struct Point
{
    int x;

    public override int GetHashCode() => x;
}");
        }
    }
}