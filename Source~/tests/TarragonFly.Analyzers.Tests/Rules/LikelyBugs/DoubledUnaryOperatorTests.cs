using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class DoubledUnaryOperatorTests
    {
        [Test]
        public void DoubledNotOrComplement_IsReportedOnTheOperators()
        {
            AnalyzerVerifier.Verify(new DoubledUnaryOperatorAnalyzer(), @"
class Player
{
    void Check(bool alive, int mask)
    {
        bool a = {|TF2035:!!|}alive;
        int b = {|TF2035:~~|}mask;
        bool c = {|TF2035:!!!|}alive;
    }
}");
        }

        [Test]
        public void ParenthesesAndOtherOperators_AreFine()
        {
            AnalyzerVerifier.Verify(new DoubledUnaryOperatorAnalyzer(), @"
class Player
{
    void Check(bool alive, int mask)
    {
        bool a = !(!alive);
        int b = - -mask;
        int c = ~(~mask);
        bool d = !alive;
    }
}");
        }
    }
}