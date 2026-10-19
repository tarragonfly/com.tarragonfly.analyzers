using NUnit.Framework;
using TarragonFly.Analyzers.Rules.DeadCode;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.DeadCode
{
    // TF1502 value stored but never read.
    public class DeadStoreTests
    {
        [Test]
        public void StoreNothingReads_IsReported()
        {
            AnalyzerVerifier.Verify(new DeadStoreAnalyzer(), @"
using System;

class Target
{
    int Cases(bool b, int y, string s)
    {
        int {|TF1502:damage = Compute()|};
        damage = 10;
        int g = 1;
        {|TF1502:g = Compute()|};
        g = 5;
        int h = 0;
        {|TF1502:h++|};
        int k;
        {|TF1502:k = Compute()|};
        {|TF1502:k = 7|};
        {|TF1502:y = b ? 1 : 2|};
        y = b ? 3 : 4;
        {|TF1502:s ??= ""x""|};
        int e = 1;
        if (b) e = 2;
        int i = 2;
        i += 3;
        return damage + g + e + i + y;
    }

    void Skipped(bool b, string text)
    {
        int unused = Compute();
        int placeholder = 0;
        placeholder = Compute();
        string nothing = null;
        nothing = text;
        int captured = Compute();
        Action read = () => Console.WriteLine(captured);
        captured = 4;
        int.TryParse(text, out int parsed);
        var (p, q) = (1, 2);
        p = 3;
        int caught = 1;
        try { caught = Compute(); }
        catch (Exception) { Console.WriteLine(caught); }
        Console.WriteLine(placeholder + nothing + p + q);
    }

    void Loop(int[] values)
    {
        int last = 0;
        foreach (int v in values) {|TF1502:last = v|};
    }

    static int Compute() => 1;
}");
        }
    }
}