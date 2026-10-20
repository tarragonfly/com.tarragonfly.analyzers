using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class StatementAfterUnbracedBodyTests
    {
        private static void Verify(string statements)
        {
            AnalyzerVerifier.Verify(new StatementAfterUnbracedBodyAnalyzer(), @"
class Player
{
    int score;

    void Die(bool dead, int[] items)
    {
" + statements + @"
    }
}");
        }

        [Test]
        public void StatementLinedUpWithTheBody_IsReported()
        {
            Verify(@"
        if (dead)
            score++;
            {|TF1015:score--;|}

        foreach (int item in items)
            score += item;

            {|TF1015:score = 0;|}

        if (dead) score++; {|TF1015:score--;|}

        if (dead) score++;
            {|TF1015:score--;|}

        if (dead)
            score++;
        else
            score--;
            {|TF1015:score++;|}
        score = 1;");
        }

        [Test]
        public void StatementBackAtTheOuterIndent_IsFine()
        {
            Verify(@"
        if (dead)
            score++;
        score--;

        if (dead)
            score++;
          score--;

        if (dead)
        {
            score++;
        }
            score--;

        do
            score++;
        while (score < 3);
            score--;");
        }

        [Test]
        public void BodyNotIndentedPastItsKeyword_IsComparedThroughToItsInnermostStatement()
        {
            Verify(@"
        if (dead)
        for (int i = 0; i < 3; i++)
            score++;

        score--;

        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            score++;
        }
        score--;

        if (dead)
        score++;
        {|TF1015:score--;|}

        if (dead)
            if (score > 0)
                score++;
            {|TF1015:score--;|}");
        }

        // Counted as characters, the 8 spaces would sit past the 3 tabs of the body.
        [Test]
        public void TabIndentation_IsCountedAsFourColumns()
        {
            AnalyzerVerifier.Verify(new StatementAfterUnbracedBodyAnalyzer(), "class Player\n{\n\tint score;\n\n\tvoid Die(bool dead)\n\t{\n\t\tif (dead)\n\t\t\tscore++;\n        score--;\n\t}\n}");
        }
    }
}