using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class EmptyStatementTests
    {
        [Test]
        public void EmptyStatements_AreReportedOnTheSemicolon()
        {
            AnalyzerVerifier.Verify(new EmptyStatementAnalyzer(), @"
class Player
{
    int score;

    void Land(bool grounded, int[] enemies)
    {
        score++;{|TF2000:;|}
        if (grounded){|TF2000:;|}
        {
            score++;
        }
        foreach (int enemy in enemies){|TF2000:;|}
        {|TF2000:;|}
    landed:
        {|TF2000:;|}
        if (score > 10) goto landed;
    }
}");
        }

        [Test]
        public void EmptyLoopBodies_AreFine()
        {
            AnalyzerVerifier.Verify(new EmptyStatementAnalyzer(), @"
class Player
{
    bool Step() => false;

    void Land()
    {
        while (Step());
        for (int i = 0; i < 3; i++);
        do ; while (Step());
    }
}");
        }
    }
}