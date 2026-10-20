using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class ReversedCompoundOperatorTests
    {
        [Test]
        public void UnaryOperatorTouchingTheEquals_IsReported()
        {
            AnalyzerVerifier.Verify(new ReversedCompoundOperatorAnalyzer(), @"
class Score
{
    int total;

    void Add(int points, bool flag)
    {
        total ={|TF2034:+|} points;
        total ={|TF2034:+|}points;
        total={|TF2034:+|} points;
        total ={|TF2034:-|} points;
        bool done ={|TF2034:!|} flag;
        done ={|TF2034:!|}flag;
        int copy ={|TF2034:+|} points;
    }
}");
        }

        [Test]
        public void NormalSpacingAndNegativeValues_AreFine()
        {
            AnalyzerVerifier.Verify(new ReversedCompoundOperatorAnalyzer(), @"
class Score
{
    int total;

    void Add(int points, bool flag)
    {
        total += points;
        total = +points;
        total = -points;
        total =-points;
        total =-1;
        total =
            -points;
        bool done = !flag;
        total =~ points;
    }
}");
        }
    }
}