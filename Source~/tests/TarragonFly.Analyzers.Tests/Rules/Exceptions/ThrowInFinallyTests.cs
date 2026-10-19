using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Exceptions;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Exceptions
{
    public class ThrowInFinallyTests
    {
        [Test]
        public void ThrowThatCanEscapeTheFinally_IsReported()
        {
            AnalyzerVerifier.Verify(new ThrowInFinallyAnalyzer(), @"
using System;

class SaveSystem
{
    void Save(bool corrupt)
    {
        try { Save(corrupt); }
        finally
        {
            if (corrupt)
            {
                {|TF3050:throw new InvalidOperationException();|}
            }
            try { Save(corrupt); }
            finally { {|TF3050:throw new InvalidOperationException();|} }
            try { {|TF3050:throw new InvalidOperationException();|} }
            catch (ArgumentException) { }
            try { {|TF3050:throw new InvalidOperationException();|} }
            catch (InvalidOperationException) when (corrupt) { }
        }
    }
}");
        }

        [Test]
        public void ThrowsCaughtInsideOrRunLater_AreFine()
        {
            AnalyzerVerifier.Verify(new ThrowInFinallyAnalyzer(), @"
using System;

class SaveSystem
{
    object Save(object file)
    {
        try { return file; }
        finally
        {
            try { throw new InvalidOperationException(); }
            catch (InvalidOperationException) { }
            try { throw new InvalidOperationException(); }
            catch (Exception) { }
            try { throw new InvalidOperationException(); }
            catch { }
            Action later = () => { throw new InvalidOperationException(); };
            object checkedFile = file ?? throw new ArgumentNullException(nameof(file));
            void Local() { throw new InvalidOperationException(); }
        }
    }
}");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new ThrowInFinallyAnalyzer(), @"
class SaveSystemTests
{
    void Saves()
    {
        try { Saves(); }
        finally { throw new System.InvalidOperationException(); }
    }
}", SnippetAssembly.Tests());
        }
    }
}