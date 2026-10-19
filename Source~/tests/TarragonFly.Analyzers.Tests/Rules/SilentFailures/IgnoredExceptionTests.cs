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
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new IgnoredExceptionAnalyzer(), Code.Replace("{|TF3004:", "").Replace("}|}", "}"), SnippetAssembly.Tests());
        }
    }
}