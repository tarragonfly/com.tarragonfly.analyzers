using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class MergeableIfTests
    {
        [Test]
        public void IfHoldingOnlyAnotherIf_IsReported()
        {
            AnalyzerVerifier.Verify(new MergeableIfAnalyzer(), @"
class Player
{
    void Jump(bool grounded, bool pressed, bool alive)
    {
        if (grounded)
        {
            // comments don't stop the merge
            {|TF1006:if|} (pressed)
            {
                alive = false;
            }
        }

        if (grounded)
            {|TF1006:if|} (pressed)
                alive = false;

        if (alive)
        {
        }
        else if (grounded)
        {
            {|TF1006:if|} (pressed) alive = false;
        }
    }
}");
        }

        [Test]
        public void ElseOnEitherIf_OrOtherStatements_PreventTheMerge()
        {
            AnalyzerVerifier.Verify(new MergeableIfAnalyzer(), @"
class Player
{
    void Jump(bool grounded, bool pressed, bool alive)
    {
        if (grounded)
        {
            if (pressed) alive = false;
            else alive = true;
        }

        if (grounded)
        {
            if (pressed) alive = false;
        }
        else
        {
            alive = true;
        }

        if (grounded)
        {
            if (pressed) alive = false;
            alive = true;
        }
    }
}");
        }

        [Test]
        public void MergeThatWouldGoOverTheConditionLimit_IsNotReported()
        {
            AnalyzerVerifier.Verify(new MergeableIfAnalyzer(), @"
class Board
{
    void Check(bool a, bool b, bool c, bool d)
    {
        if (a)
        {
            if (b || c || d) a = false;
        }

        if (a)
        {
            {|TF1006:if|} (b || c) a = false;
        }
    }
}", SnippetAssembly.WithSettings("max_condition_operators = 2"));
        }
    }
}