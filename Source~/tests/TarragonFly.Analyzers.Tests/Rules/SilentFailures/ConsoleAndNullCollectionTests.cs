using NUnit.Framework;
using TarragonFly.Analyzers.Rules.SilentFailures;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.SilentFailures
{
    // TF3000 Console output, TF3002 null returned instead of an empty collection.
    public class ConsoleAndNullCollectionTests
    {
        [Test]
        public void ConsoleWrite_IsReportedInGameCode()
        {
            const string Code = @"
using System;
using System.IO;

class Target
{
    void Check(TextWriter writer)
    {
        {|TF3000:Console.WriteLine|}(""hit"");
        {|TF3000:Console.Write|}(1);
        Console.Error.WriteLine(""e"");
        writer.WriteLine(""ok"");
        UnityEngine.Debug.Log(""fine"");
    }
}";
            AnalyzerVerifier.Verify(new ConsoleOutputAnalyzer(), Code);
            AnalyzerVerifier.Verify(new ConsoleOutputAnalyzer(), Code.Replace("{|TF3000:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void NullReturnedForACollection_IsReported()
        {
            AnalyzerVerifier.Verify(new NullCollectionReturnedAnalyzer(), @"
using System;
using System.Collections.Generic;

class Loot
{
    public int[] Drops(bool b)
    {
        if (b) return {|TF3002:null|};
        return new int[0];
    }

    public List<int> Items() => {|TF3002:null|};
    public IList<int> Slots() => {|TF3002:default|};
    public Dictionary<int, int> Map => {|TF3002:null|};
    public int[] Getter { get { return {|TF3002:null|}; } }
    public int[] Maybe(bool b) => b ? {|TF3002:null|} : new int[0];
    private int[] Private() => {|TF3002:null|};

    public string Name() => null;
    public object Plain() => null;

    public List<int> Lambda()
    {
        Func<List<int>> make = () => null;
        List<int> Local() => null;
        return make() ?? Local();
    }

    public IEnumerable<int> Iterator()
    {
        yield break;
    }
}");
        }
    }
}