using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class ForConditionIgnoresCounterTests
    {
        [Test]
        public void UpdateThatChangesNothingTheConditionTests_IsReported()
        {
            AnalyzerVerifier.Verify(new ForConditionIgnoresCounterAnalyzer(), @"
class Grid
{
    int ticks;

    void Tick() { ticks++; }

    void Fill(int[] cells)
    {
        int j = 0;
        for (int i = 0; {|TF2010:i < cells.Length|}; j++) { i++; }
        for (int i = 0; {|TF2010:i < 10|}; Tick()) { i++; }
        for (int i = 0; {|TF2010:cells[i] > 0|}; ticks++) { i++; }
        {|TF2010:for|} (int i = 0; ; j++) { if (i++ > 3) break; }
    }
}");
        }

        [Test]
        public void ConstantCondition_IsReportedOnTheForKeyword()
        {
            AnalyzerVerifier.Verify(new ForConditionIgnoresCounterAnalyzer(), @"
class Grid
{
    const int Limit = 3;

    void Fill()
    {
        {|TF2010:for|} (int i = 0; false; i++) { }
        {|TF2010:for|} (int i = 0; Limit > 0; i++) { break; }
    }
}");
        }

        [Test]
        public void UpdateOfAnyTestedVariable_IsFine()
        {
            AnalyzerVerifier.Verify(new ForConditionIgnoresCounterAnalyzer(), @"
class Grid
{
    static void Next(ref int i) { i++; }

    void Fill(int[] cells)
    {
        int j = 0;
        for (int i = 0; i < 10 && j < 5; j++) { }
        for (int i = 0, k = 0; i < 10; k++, i++) { }
        for (int i = 0; i < 10; Next(ref i)) { }
        for (int i = 0; i < 10; i = i + 1) { }
        for (int i = 0; i < 10;) { i++; }
    }
}");
        }
    }
}