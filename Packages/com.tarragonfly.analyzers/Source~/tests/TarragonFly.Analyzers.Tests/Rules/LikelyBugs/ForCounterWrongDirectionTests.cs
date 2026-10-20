using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class ForCounterWrongDirectionTests
    {
        [Test]
        public void CounterMovingAwayFromTheEnd_IsReportedOnTheUpdate()
        {
            AnalyzerVerifier.Verify(new ForCounterWrongDirectionAnalyzer(), @"
class Wave
{
    void Spawn(int count)
    {
        for (int i = 0; i < count; {|TF2022:i--|}) { }
        for (int i = count; i > 0; {|TF2022:i++|}) { }
        for (int i = count; i >= 0; {|TF2022:i += 1|}) { }
        for (int i = 0; i <= count; {|TF2022:i -= 2|}) { }
        for (int i = 0; count > i; {|TF2022:--i|}) { }
        for (int i = 0; i < count; {|TF2022:i = i - 1|}) { }
        for (float t = 0; t < 1; {|TF2022:t -= 0.1f|}) { }
    }
}");
        }

        [Test]
        public void RightDirectionOrUnknownStep_IsFine()
        {
            AnalyzerVerifier.Verify(new ForCounterWrongDirectionAnalyzer(), @"
class Wave
{
    void Spawn(int count, int step)
    {
        for (int i = 0; i < count; i++) { }
        for (int i = count; i > 0; i--) { }
        for (int i = 0; i != count; i--) { }
        for (int i = 0; i < count; i += step) { }
        for (int i = 0; i < count; i -= -1) { }
        for (int i = 0; i < count; i = 1 + i) { }
    }
}");
        }
    }
}