using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Settings;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1025 'var' hides the type.
    public class VarHidesTypeTests
    {
        private const string Code = @"
using System.Collections.Generic;
using System.Linq;

class Spawner
{
    void Spawn(List<int> ids, Dictionary<string, int> counts)
    {
        {|TF1025:var|} total = ids.Count;
        for ({|TF1025:var|} i = 0; i < total; i++) { }
        foreach ({|TF1025:var|} id in ids) { }
        using ({|TF1025:var|} reader = new System.IO.StringReader(""a"")) { }
        using {|TF1025:var|} writer = new System.IO.StringWriter();
        if (counts.TryGetValue(""a"", out {|TF1025:var|} count)) { }
        {|TF1025:var|} pair = (1, 2);
        {|TF1025:var|} lookup = ids.ToDictionary(id => id, id => id.ToString());

        int explicitTotal = ids.Count;
        foreach (int explicitId in ids) { }
        var anonymous = new { Name = ""a"", Count = 1 };
        var anonymousList = ids.Select(id => new { Id = id });
        var anonymousArray = new[] { new { Id = 1 } };
        var anonymousPair = (1, new { Id = 1 });
        foreach (var item in anonymousList) { }
        var (first, second) = pair;
        if (total is var any) { }
        if (counts.TryGetValue(""b"", out var _)) { }
    }
}";

        [Test]
        public void WithReportVarAll_EveryVarThatCanBeWritten_IsReported()
        {
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Code, SnippetAssembly.WithSettings("report_var = all"));
        }

        [Test]
        public void WithoutTheSetting_NothingIsReported()
        {
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Markup.Without(Code, "TF1025"));
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Markup.Without(Code, "TF1025"), SnippetAssembly.WithSettings("report_var = none"));
        }

        [Test]
        public void WithReportVarGame_GameCodeIsReported_AndTestCodeIsNot()
        {
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Code, SnippetAssembly.WithSettings("report_var = game"));
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Markup.Without(Code, "TF1025"), SnippetAssembly.Tests("report_var = game"));
        }

        [Test]
        public void WithReportVarAll_TestCodeIsReportedToo()
        {
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), Code, SnippetAssembly.Tests("report_var = all"));
        }

        // With a type named var in scope, var always means that type, so there's nothing hidden.
        [Test]
        public void TypeNamedVar_IsNotReported()
        {
            AnalyzerVerifier.Verify(new VarHidesTypeAnalyzer(), @"
class var { }

class Holder
{
    void Run()
    {
        var typed = new var();
        foreach (var item in new var[0]) { }
    }
}", SnippetAssembly.WithSettings("report_var = all"));
        }

        [Test]
        public void UnknownValue_IsReportedInTheSettingsFile()
        {
            AnalyzerVerifier.Verify(new SettingsFileAnalyzer(), "class Player { }", SnippetAssembly.WithSettings("{|TF0000:report_var = sometimes|}\n"));
        }
    }
}
