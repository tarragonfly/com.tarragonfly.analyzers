using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Settings;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1025 'var' instead of the type name.
    public class VarInsteadOfTypeTests
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
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Code, SnippetAssembly.WithSettings("report_var = all"));
        }

        [Test]
        public void WithoutTheSetting_NothingIsReported()
        {
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Markup.Without(Code, "TF1025"));
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Markup.Without(Code, "TF1025"), SnippetAssembly.WithSettings("report_var = none"));
        }

        [Test]
        public void WithReportVarGame_GameCodeIsReported_AndTestCodeIsNot()
        {
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Code, SnippetAssembly.WithSettings("report_var = game"));
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Markup.Without(Code, "TF1025"), SnippetAssembly.Tests("report_var = game"));
        }

        [Test]
        public void WithReportVarAll_TestCodeIsReportedToo()
        {
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), Code, SnippetAssembly.Tests("report_var = all"));
        }

        // Writing these types on the left would just say them twice. new T() still gets reported,
        // because T x = new() says it once.
        [Test]
        public void TypeNamedOnTheLine_IsNotReported()
        {
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

enum Side { Left, Right }
class Part { }
class Wheel : Part { public static Wheel Spare { get; } = new Wheel(); }
class Garage<T> { public static T Instance { get; } }

class Car
{
    T Get<T>() where T : Part => null;
    T[] GetAll<T>() where T : Part => new T[0];
    Task<T> LoadAsync<T>() where T : Part => null;
    TValue Find<TKey, TValue>(TKey key) => default;
    int Count<T>() => 0;
    static Part Spare() => null;

    async Task Build(object item, Part part, List<Part> parts, Car other, CancellationToken token)
    {
        var wheel = (Wheel)part;
        var maybeWheel = item as Wheel;
        var wrapped = ((Wheel)part);
        var grid = new int[3, 4];
        var names = new string[2];
        var empty = default(Wheel);
        var found = Get<Wheel>();
        var foundHere = this.Get<Wheel>();
        var foundOther = other?.Get<Wheel>();
        var loaded = await LoadAsync<Wheel>();
        var size = Find<string, int>(""a"");
        var delay = TimeSpan.FromSeconds(1);
        var none = TimeSpan.Zero;
        var spareWheel = Wheel.Spare;
        var side = Side.Left;
        var parked = Garage<Wheel>.Instance;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        for (var next = Side.Left; next <= Side.Right; next++) { }
        foreach (var each in GetAll<Wheel>()) { }
        foreach (var each in parts.OfType<Wheel>()) { }
        var forgiven = Get<Wheel>()!;
        var grouped = (Get<Wheel>());
        var loadedGrouped = await (LoadAsync<Wheel>());
        var spareForgiven = Wheel.Spare!;
        foreach (var each in (GetAll<Wheel>())) { }

        {|TF1025:var|} built = new Wheel();
        {|TF1025:var|} all = GetAll<Wheel>();
        {|TF1025:var|} count = Count<int>();
        {|TF1025:var|} spare = Spare();
        {|TF1025:var|} numbers = new[] { 1, 2 };
        {|TF1025:var|} seconds = TimeSpan.FromSeconds(1).TotalSeconds;
        foreach ({|TF1025:var|} each in all) { }
    }
}", SnippetAssembly.WithSettings("report_var = all"));
        }

        // With a type named var in scope, var always means that type, so there's nothing hidden.
        [Test]
        public void TypeNamedVar_IsNotReported()
        {
            AnalyzerVerifier.Verify(new VarInsteadOfTypeAnalyzer(), @"
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
