using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class IfAfterClosingBraceTests
    {
        [Test]
        public void IfRightAfterAClosingBrace_IsReported()
        {
            AnalyzerVerifier.Verify(new IfAfterClosingBraceAnalyzer(), @"
class Player
{
    void Hit(bool shielded, bool dodged)
    {
        if (shielded)
        {
            dodged = false;
        } {|TF1021:if|} (dodged)
        {
            shielded = false;
        }

        while (shielded) { shielded = false; } {|TF1021:if|} (dodged) { }
    }
}");
        }

        [Test]
        public void ElseIfAndIfOnItsOwnLine_AreFine()
        {
            AnalyzerVerifier.Verify(new IfAfterClosingBraceAnalyzer(), @"
class Player
{
    void Hit(bool shielded, bool dodged)
    {
        if (shielded)
        {
            dodged = false;
        } else if (dodged)
        {
            shielded = false;
        }
        if (dodged)
        {
        }
        if (shielded) dodged = true; if (dodged) { }
    }
}");
        }
    }
}