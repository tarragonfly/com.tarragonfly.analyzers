using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class DeepNestingTests
    {
        [Test]
        public void FourthLevel_IsReported_ButNotWhatIsInsideIt()
        {
            AnalyzerVerifier.Verify(new DeepNestingAnalyzer(), @"
class Spawner
{
    void Spawn(bool a, int[] waves)
    {
        if (a)
        {
            foreach (int wave in waves)
            {
                while (a)
                {
                    {|TF1003:try|}
                    {
                        if (a) a = false;
                    }
                    catch (System.InvalidOperationException)
                    {
                        a = false;
                    }
                }
            }
        }
    }
}");
        }

        [Test]
        public void TryWithOnlyFinally_AddsNoLevel()
        {
            AnalyzerVerifier.Verify(new DeepNestingAnalyzer(), @"
class GoalsView
{
    void Refresh(bool a, int[] goals)
    {
        try
        {
            while (a)
            {
                foreach (int goal in goals)
                {
                    if (goal > 0) a = false;
                    if (goal < 0) a = true;
                }
            }
        }
        finally
        {
            a = false;
        }
    }
}");
        }

        [Test]
        public void ElseIfChain_AddsNoLevel_AndLockOrUsingDontCount()
        {
            AnalyzerVerifier.Verify(new DeepNestingAnalyzer(), @"
class Spawner
{
    void Spawn(int a, System.IDisposable d)
    {
        if (a == 1)
        {
        }
        else if (a == 2)
        {
            lock (this)
            {
                using (d)
                {
                    switch (a)
                    {
                        case 2:
                            for (int i = 0; i < a; i++)
                            {
                                a--;
                            }
                            break;
                    }
                }
            }
        }
    }
}");
        }

        [Test]
        public void Lambdas_KeepCounting()
        {
            AnalyzerVerifier.Verify(new DeepNestingAnalyzer(), @"
class Spawner
{
    void Spawn(bool a)
    {
        if (a)
        {
            if (a)
            {
                System.Action act = () =>
                {
                    if (a)
                    {
                        {|TF1003:if|} (a) a = false;
                    }
                };
            }
        }
    }
}");
        }

        [Test]
        public void Limit_ComesFromSettings()
        {
            AnalyzerVerifier.Verify(new DeepNestingAnalyzer(), @"
class Spawner
{
    void Spawn(bool a)
    {
        if (a)
        {
            {|TF1003:while|} (a) a = false;
        }
    }
}", SnippetAssembly.WithSettings("max_nesting = 1"));
        }
    }
}