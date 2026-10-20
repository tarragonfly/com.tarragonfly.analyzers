using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2003 float equality, TF2014 shift by zero or past the width, TF2015 integer division to float,
    // TF2017 odd check with %, TF2018 number compared past its range, TF2025 unchecked Sum, TF2029
    // no-effect bit operation, TF2032 NaN comparison, TF2047 bitwise non-[Flags] enum, TF2051 dynamic
    // shift count, TF2061 count comparison that is always the same.
    public class NumberTests
    {
        [Test]
        public void ExactFloatComparisons_AreReported()
        {
            AnalyzerVerifier.Verify(new FloatEqualityAnalyzer(), @"
class Target
{
    void Check(float a, float b, double d, float? n, int i)
    {
        bool r1 = a {|TF2003:==|} b;
        bool r2 = d {|TF2003:!=|} 2.5;
        bool r3 = n {|TF2003:==|} b;
        bool r4 = {|TF2003:a <= b && a >= b|};
        bool r5 = {|TF2003:a < b || a > b|};
        bool r6 = a.{|TF2003:Equals|}(b);
        bool r7 = a == 0f;
        bool r8 = a <= b && a >= 1f;
        bool r9 = i == 2;
    }
}");
        }

        [Test]
        public void NullableComparedWithNullOrAChar_IsFine()
        {
            AnalyzerVerifier.Verify(new FloatEqualityAnalyzer(), @"
class Target
{
    bool Check(double? time, float f) => time == null || null != time || f {|TF2003:==|} 'a';
}");
        }

        [Test]
        public void EqualsWithNaNZeroOrObject_IsFine()
        {
            AnalyzerVerifier.Verify(new FloatEqualityAnalyzer(), @"
class Target
{
    void Check(float a, float b)
    {
        bool r1 = a.Equals(float.NaN);
        bool r2 = float.NaN.Equals(a);
        bool r3 = a.Equals(0f);
        bool r4 = a.Equals((object)b);
    }
}");
        }

        [Test]
        public void ShiftsPastTheWidth_AreReported()
        {
            AnalyzerVerifier.Verify(new ShiftByZeroOrPastWidthAnalyzer(), @"
class Target
{
    void Check(int i, long l, byte b, int n)
    {
        int r1 = {|TF2014:i << 32|};
        long r2 = {|TF2014:l >> 64|};
        long r3 = l << 32;
        int r4 = {|TF2014:b << 33|};
        {|TF2014:i <<= 40|};
        int r5 = i << n;
        int r6 = i >> -1;
        int r7 = i << 31;
    }
}");
        }

        [Test]
        public void ZeroShift_IsReportedUnlessPartOfAMaskList()
        {
            AnalyzerVerifier.Verify(new ShiftByZeroOrPastWidthAnalyzer(), @"
[System.Flags]
enum Layer { Ground = 1 << 0, Water = 2 }

class Target
{
    const int LayerA = 1 << 0;
    const int LayerB = 1 << 1;

    int Alone(int x)
    {
        return {|TF2014:x << 0|};
    }

    int NextToOtherMath(int x)
    {
        int f0 = {|TF2014:x << 0|};
        int f1 = x + 1;
        return f0 | f1;
    }

    int Masks(int x)
    {
        int f0 = x << 0;
        int f1 = x << 1;
        return f0 | f1;
    }

    int SameStatement(int x)
    {
        return (x << 0) | (x << 1);
    }
}");
        }

        [Test]
        public void ZeroShiftByANamedConstant_IsFine()
        {
            AnalyzerVerifier.Verify(new ShiftByZeroOrPastWidthAnalyzer(), @"
class Packed
{
    const int CurrentOffset = 0;
    long bits;

    long Current => (bits >> CurrentOffset) & 0xFF;
}");
        }

        [Test]
        public void IntegerDivisionStoredAsFloat_IsReported()
        {
            AnalyzerVerifier.Verify(new IntegerDivisionToFloatAnalyzer(), @"
class Target
{
    void Take(double value) { }

    void Check(int collected, int total)
    {
        float progress = {|TF2015:collected / total|};
        double ratio;
        ratio = {|TF2015:collected / total|};
        Take({|TF2015:collected / total|});
        float cast = (float)collected / total;
        int whole = collected / total;
        float nested = collected / total + 0.5f;
    }
}");
        }

        [Test]
        public void OddCheckOnSignedValue_IsReported()
        {
            AnalyzerVerifier.Verify(new OddCheckWithModulusAnalyzer(), @"
class Target
{
    void Check(int turn, uint u, int[] items, System.Collections.Generic.List<int> list)
    {
        bool r1 = {|TF2017:turn % 2 == 1|};
        bool r2 = {|TF2017:1 != turn % 2|};
        bool r3 = turn % 2 == 0;
        bool r4 = u % 2 == 1;
        bool r5 = items.Length % 2 == 1;
        bool r6 = list.Count % 2 == 1;
    }
}");
        }

        [Test]
        public void NumberComparedWithConstantOutsideItsRange_IsReported()
        {
            const string Code = @"
class Target
{
    void Check(float distance, double d, long ticks, ulong id, char letter, int count, long? maybe)
    {
        bool r1 = {|TF2018:distance > double.MaxValue|};
        bool r2 = distance > float.MaxValue;
        bool r3 = d > double.MaxValue;
        bool r4 = {|TF2018:ticks > 1e19|};
        bool r5 = {|TF2018:double.MinValue < ticks|};
        bool r6 = {|TF2018:ticks > float.MaxValue|};
        bool r7 = {|TF2018:id < -1.0|};
        bool r8 = id >= 0.0;
        bool r9 = {|TF2018:letter > 70000.0|};
        bool r10 = {|TF2018:count > 1e10|};
        bool r11 = ticks > 9.2e18;
        bool r12 = maybe < double.MinValue;
        bool r13 = distance > double.PositiveInfinity;
    }
}";
            AnalyzerVerifier.Verify(new NumberComparedPastItsRangeAnalyzer(), Code);
            AnalyzerVerifier.Verify(new NumberComparedPastItsRangeAnalyzer(), Code.Replace("{|TF2018:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void IntegerSumInUncheckedContext_IsReported()
        {
            AnalyzerVerifier.Verify(new UncheckedSumAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> scores, List<long> totals, List<float> weights, IQueryable<int> query)
    {
        unchecked
        {
            int a = scores.{|TF2025:Sum|}();
            long b = totals.{|TF2025:Sum|}();
            float c = weights.Sum();
            int d = checked(scores.Sum());
            int g = query.{|TF2025:Sum|}(x => x);
            int h = Queryable.{|TF2025:Sum|}(scores.AsQueryable());
        }
        int e = unchecked(scores.{|TF2025:Sum|}());
        int f = scores.Sum();
    }
}");
        }

        [Test]
        public void BitOperationWithoutEffect_IsReported()
        {
            AnalyzerVerifier.Verify(new NoEffectBitOperationAnalyzer(), @"
class Target
{
    const int Zero = 0;

    void Check(int x, bool flag)
    {
        int r1 = x {|TF2029:^ 0|};
        int r2 = x {|TF2029:& -1|};
        int r3 = {|TF2029:0 ^|} x;
        bool r4 = flag {|TF2029:^ false|};
        {|TF2029:x ^= 0|};
        int r5 = x ^ Zero;
        int r6 = x & 0xFF;
        int r7 = x & 0;
        bool r8 = flag & true;
    }
}");
        }

        [Test]
        public void ComparisonWithNaN_IsReported()
        {
            AnalyzerVerifier.Verify(new NaNComparisonAnalyzer(), @"
class Target
{
    void Check(float speed, double d)
    {
        bool r1 = {|TF2032:speed == float.NaN|};
        bool r2 = {|TF2032:double.NaN != d|};
        bool r3 = {|TF2032:speed < float.NaN|};
        bool r4 = float.IsNaN(speed);
        bool r5 = float.NaN.Equals(speed);
    }
}");
        }

        [Test]
        public void BitwiseOperationOnPlainEnum_IsReported()
        {
            AnalyzerVerifier.Verify(new BitwiseNonFlagsEnumAnalyzer(), @"
enum Team { Red, Blue }

[System.Flags]
enum Mask { None = 0, A = 1, B = 2 }

class Target
{
    void Check(Team team, Mask mask)
    {
        Team r1 = Team.Red {|TF2047:&|} Team.Blue;
        Team r2 = team {|TF2047:^|} Team.Blue;
        team {|TF2047:&=|} Team.Red;
        Mask r3 = mask & Mask.A;
        Team r4 = ~team;
        bool r5 = team == Team.Red;
    }
}");
        }

        [Test]
        public void DynamicShiftByNonInt_IsReported()
        {
            AnalyzerVerifier.Verify(new DynamicShiftCountAnalyzer(), @"
class Target
{
    void Check(dynamic value, long big, byte small, int n)
    {
        dynamic r1 = value << {|TF2051:big|};
        dynamic r2 = value >> {|TF2051:1.5|};
        dynamic r3 = value << {|TF2051:null|};
        dynamic r4 = value << small;
        dynamic r5 = value << n;
        value <<= {|TF2051:big|};
        int r6 = n << 1;
    }
}");
        }

        [Test]
        public void CountComparedWithZeroOrBelow_IsReported()
        {
            AnalyzerVerifier.Verify(new CountComparisonAlwaysSameAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Squad
{
    public int Count { get; set; }
}

class Target
{
    void Check(List<int> enemies, string path, int[] items, Squad squad)
    {
        bool r1 = {|TF2061:enemies.Count >= 0|};
        bool r2 = {|TF2061:path.Length < 0|};
        bool r3 = {|TF2061:0 <= items.Length|};
        bool r4 = {|TF2061:enemies.Count() == -1|};
        bool r5 = {|TF2061:items.LongLength > -1|};
        bool r6 = {|TF2061:enemies.LongCount() >= 0|};
        bool r7 = enemies.Count > 0;
        bool r8 = path.Length == 0;
        bool r9 = enemies.Count >= 1;
        bool r10 = squad.Count >= 0;
    }
}");
        }
    }
}