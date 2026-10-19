using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Performance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Performance
{
    // TF8100 constant array built on every call.
    public class PerCallAllocationTests
    {
        [Test]
        public void ConstantArrayArgument_IsReported()
        {
            const string Code = @"
using System;

enum Channel { Music, Sfx }

class Parser
{
    const string Newline = ""\n"";
    static readonly string[] Separators = { ""\r\n"", ""\n"" };
    static readonly int[] Built = Make(new[] { 1, 2 });
    readonly int[] perInstance = Make(new[] { 3 });

    static Parser()
    {
        Make(new[] { 4 });
    }

    string[] Lines(string text)
    {
        Make({|TF8100:new[] { 1, 2, 3 }|});
        Make({|TF8100:new int[] { 4 }|});
        Use({|TF8100:new[] { Channel.Music, Channel.Sfx }|});
        Func<string[]> later = () => text.Split({|TF8100:new[] { ""\r\n"", Newline }|}, StringSplitOptions.None);
        Make(new[] { text.Length });
        Make(new int[0]);
        Make(new int[] { });
        Spread(new[] { 1, 2 });
        Span(new[] { 1, 2 });
        System.Collections.Generic.HashSet<int> set = new System.Collections.Generic.HashSet<int>(new[] { 1, 2 });
        return text.Split(Separators, StringSplitOptions.None);
    }

    int[] Property => Make({|TF8100:new[] { 5 }|});

    static int[] Make(int[] items) => items;
    static void Use(Channel[] channels) { }
    static void Spread(params int[] items) { }
    static void Span(ReadOnlySpan<int> items) { }
}";
            AnalyzerVerifier.Verify(new ConstantArrayArgumentAnalyzer(), Code);
            AnalyzerVerifier.Verify(new ConstantArrayArgumentAnalyzer(), Code.Replace("{|TF8100:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }
    }
}