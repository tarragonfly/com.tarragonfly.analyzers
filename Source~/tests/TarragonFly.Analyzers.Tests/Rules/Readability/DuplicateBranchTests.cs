using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class DuplicateBranchTests
    {
        private static void Verify(string statements, SnippetAssembly assembly = null)
        {
            AnalyzerVerifier.Verify(new DuplicateBranchAnalyzer(), @"
class Menu
{
    int score;

    void Use(int a, int b) { }

    void Click(int button, object target)
    {
" + statements + @"
    }
}

class PlayButton { }
class QuitButton { }", assembly);
        }

        [Test]
        public void LaterCopyOfAnIfBranch_IsReported()
        {
            Verify(@"
        if (button == 1)
        {
            score += 1;
            Use(score, 0);
        }
        else if (button == 2)
        {
            score += 2;
        }
        else if (button == 3)
        {|TF1014:{
            // same code, different comment
            score += 1;
            Use(score,
                0);
        }|}");
        }

        [Test]
        public void LaterCopyOfASwitchSection_IsReported()
        {
            Verify(@"
        switch (button)
        {
            case 1:
                score += 1;
                Use(score, 0);
                break;
            case 2:
                score += 2;
                break;
            {|TF1014:case 3:
                score += 1; Use(score, 0);
                break;|}
            default:
                score++;
                break;
        }");
        }

        [Test]
        public void ShortBranches_AreSkipped()
        {
            Verify(@"
        if (button == 1) { score++; }
        else if (button == 2) { score++; }
        else { score--; }

        switch (button)
        {
            case 1:
                score++;
                break;
            case 2:
                score++;
                break;
            default:
                score--;
                break;
        }");
        }

        [Test]
        public void ShortBranches_AreReported_WhenAllAreTheSameWithoutAnElse()
        {
            Verify(@"
        if (button == 1) { score++; }
        else if (button == 2) {|TF1014:{ score++; }|}

        switch (button)
        {
            case 1:
                score++;
                break;
            {|TF1014:case 2:
                score++;
                break;|}
        }

        switch (button)
        {
            case 1:
                return;
            case 2:
                return;
        }");
        }

        [Test]
        public void UnbracedIfBranches_AreNotCompared()
        {
            Verify(@"
        if (button == 1)
            Use(score,
                0);
        else if (button == 2)
            Use(score,
                0);");
        }

        // ScenarioReferenceRenameHelper.cs: the same text works on a different type in each case.
        [Test]
        public void SectionsDeclaringPatternVariables_AreNotCompared()
        {
            Verify(@"
        switch (target)
        {
            case PlayButton play:
                Use(play.GetHashCode(), 0);
                score++;
                break;
            case QuitButton play:
                Use(play.GetHashCode(), 0);
                score++;
                break;
        }");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            Verify(@"
        if (button == 1) { score += 1; Use(score, 0); }
        else { score += 1; Use(score, 0); }", SnippetAssembly.Tests());
        }
    }
}