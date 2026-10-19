using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Exceptions;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Exceptions
{
    public class RethrowResetsStackTraceTests
    {
        [Test]
        public void ThrowingTheCaughtException_IsReported()
        {
            AnalyzerVerifier.Verify(new RethrowResetsStackTraceAnalyzer(), @"
using System;

class SaveSystem
{
    void Save()
    {
        try { Save(); }
        catch (Exception e)
        {
            if (e.Message.Length > 0) {|TF3051:throw e;|}
            e = new InvalidOperationException();
            {|TF3051:throw e;|}
        }
    }
}");
        }

        [Test]
        public void RethrowWrappingAndOuterCatchVariables_AreFine()
        {
            AnalyzerVerifier.Verify(new RethrowResetsStackTraceAnalyzer(), @"
using System;

class SaveSystem
{
    void Save()
    {
        try { Save(); }
        catch (Exception e)
        {
            Exception other = e;
            try { Save(); }
            catch (Exception) { throw e; }
            Action later = () => { throw e; };
            if (other != null) throw new InvalidOperationException(""wrapped"", e);
            throw;
        }
    }
}");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new RethrowResetsStackTraceAnalyzer(), @"
class SaveSystemTests
{
    void Saves()
    {
        try { Saves(); }
        catch (System.Exception e) { throw e; }
    }
}", SnippetAssembly.Tests());
        }
    }
}