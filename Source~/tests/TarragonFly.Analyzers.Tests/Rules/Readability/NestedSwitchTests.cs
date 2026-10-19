using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class NestedSwitchTests
    {
        [Test]
        public void SwitchStatementInsideAnother_IsReported_LambdasIncluded()
        {
            AnalyzerVerifier.Verify(new NestedSwitchAnalyzer(), @"
class Enemy
{
    int Think(int state, int mood)
    {
        switch (state)
        {
            case 0:
                {|TF1013:switch|} (mood)
                {
                    case 1: return 1;
                }
                break;
            case 1:
                System.Func<int, int> f = m =>
                {
                    {|TF1013:switch|} (m)
                    {
                        default: return 2;
                    }
                };
                return f(mood);
        }
        return 0;
    }
}");
        }

        [Test]
        public void SwitchExpressions_AreNotChecked()
        {
            AnalyzerVerifier.Verify(new NestedSwitchAnalyzer(), @"
class Enemy
{
    int Think(int state, int mood)
    {
        switch (state)
        {
            case 0:
                return mood switch { 1 => 1, _ => 0 };
        }
        return state switch { 1 => mood switch { 2 => 3, _ => 4 }, _ => 0 };
    }
}");
        }
    }
}