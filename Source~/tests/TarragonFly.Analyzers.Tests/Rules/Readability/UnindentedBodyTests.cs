using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class UnindentedBodyTests
    {
        [Test]
        public void BodyNotIndentedPastItsKeyword_IsReportedOnTheHeader()
        {
            AnalyzerVerifier.Verify(new UnindentedBodyAnalyzer(), @"
class Player
{
    int score;

    void Pause(bool paused, int[] items)
    {
        {|TF1022:if (paused)|}
        return;

        {|TF1022:if (paused)|}
      return;

        if (paused)
            score++;
        {|TF1022:else|}
        score--;

        if (paused)
            score++;
        {|TF1022:else if (!paused)|}
        score--;

        {|TF1022:foreach (int item in items)|}
        score += item;

        {|TF1022:do|}
        score++;
        while (score < 3);
    }
}");
        }

        [Test]
        public void IndentedSameLineOrBracedBodies_AreFine()
        {
            AnalyzerVerifier.Verify(new UnindentedBodyAnalyzer(), @"
class Player
{
    int score;

    void Pause(bool paused)
    {
        if (paused)
            return;
        if (paused) score++;
        if (paused)
        {
        score++;
        }
        while (paused) paused = false;
    }
}");
        }

        // Goblins.cs in the Spine examples: tab-indented code that looks right in an editor.
        [Test]
        public void TabIndentedBodyUnderASpaceIndentedElse_IsFine()
        {
            AnalyzerVerifier.Verify(new UnindentedBodyAnalyzer(), "class Player\n{\n\tint score;\n\n\tvoid Pause(bool paused)\n\t{\n\t\tif (paused) {\n\t\t\tscore++;\n\t\t} else\n\t\t\tscore--;\n\t}\n}");
        }
    }
}