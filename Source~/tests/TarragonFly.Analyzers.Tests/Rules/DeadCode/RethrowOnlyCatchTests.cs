using NUnit.Framework;
using TarragonFly.Analyzers.Rules.DeadCode;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.DeadCode
{
    public class RethrowOnlyCatchTests
    {
        private static void Verify(string statements, SnippetAssembly assembly = null)
        {
            AnalyzerVerifier.Verify(new RethrowOnlyCatchAnalyzer(), @"
using System;
using System.IO;

class SaveSystem
{
    void Save()
    {
" + statements + @"
    }
}", assembly);
        }

        [Test]
        public void CatchThatOnlyRethrows_IsReported()
        {
            Verify(@"
        try { Save(); }
        {|TF1504:catch (IOException)
        {
            // just rethrow for now
            throw;
        }|}

        try { Save(); }
        {|TF1504:catch { throw; }|}");
        }

        [Test]
        public void RethrowThatKeepsALaterCatchFromHandlingIt_IsFine()
        {
            Verify(@"
        try { Save(); }
        catch (IOException) { throw; }
        catch (Exception e) { Console.WriteLine(e); }

        try { Save(); }
        catch (IOException) { throw; }
        catch { Console.WriteLine(); }");
        }

        [Test]
        public void LaterCatchOfAnUnrelatedType_DoesntKeepIt()
        {
            Verify(@"
        try { Save(); }
        {|TF1504:catch (IOException) { throw; }|}
        catch (InvalidOperationException e) { Console.WriteLine(e); }
        {|TF1504:catch (Exception) { throw; }|}");
        }

        [Test]
        public void FiltersLoggingAndThrowE_AreNotOnlyRethrows()
        {
            Verify(@"
        try { Save(); }
        catch (Exception e) when (e.Message.Length > 0) { throw; }

        try { Save(); }
        catch (Exception e) { Console.WriteLine(e); throw; }

        try { Save(); }
        catch (IOException) { { throw; } }");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            Verify(@"
        try { Save(); }
        catch { throw; }", SnippetAssembly.Tests());
        }
    }
}