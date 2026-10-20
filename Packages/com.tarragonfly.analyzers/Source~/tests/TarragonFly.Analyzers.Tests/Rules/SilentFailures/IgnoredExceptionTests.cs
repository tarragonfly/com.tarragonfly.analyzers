using NUnit.Framework;
using TarragonFly.Analyzers.Rules.SilentFailures;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.SilentFailures
{
    public class IgnoredExceptionTests
    {
        private const string Code = @"
using System;

class SaveSystem
{
    void Load()
    {
        try { Load(); }
        {|TF3004:catch (Exception) { }|}

        try { Load(); }
        {|TF3004:catch
        {
        }|}

        try { Load(); }
        catch (Exception)
        {
            // the save file is optional
        }

        try { Load(); }
        catch (InvalidOperationException) when (DateTime.Now.Year > 2000) { }

        try { Load(); }
        catch (Exception e) { Console.WriteLine(e); }
    }
}";

        [Test]
        public void EmptyCatch_IsReported_UnlessCommentedOrFiltered()
        {
            AnalyzerVerifier.Verify(new IgnoredExceptionAnalyzer(), Code);
        }

        [Test]
        public void CancellationCatches_AreFine()
        {
            AnalyzerVerifier.Verify(new IgnoredExceptionAnalyzer(), @"
using System;
using System.Threading.Tasks;

class AsyncOperationCanceledException : OperationCanceledException { }
class LoadCanceledError : Exception { }

class Loader
{
    void Load()
    {
        try { Load(); }
        catch (OperationCanceledException) { }

        try { Load(); }
        catch (TaskCanceledException e) { }

        try { Load(); }
        catch (AsyncOperationCanceledException) { }

        try { Load(); }
        {|TF3004:catch (LoadCanceledError) { }|}

        try { Load(); }
        {|TF3004:catch (InvalidOperationException) { }|}
    }
}");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new IgnoredExceptionAnalyzer(), Code.Replace("{|TF3004:", "").Replace("}|}", "}"), SnippetAssembly.Tests());
        }
    }
}