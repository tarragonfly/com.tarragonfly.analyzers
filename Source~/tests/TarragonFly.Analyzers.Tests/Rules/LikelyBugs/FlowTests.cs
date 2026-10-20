using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2002 overwritten before use, TF2005 loop that runs once, TF2016 endless loop or recursion,
    // TF2039 returning what a using disposes, TF2054 field never assigned, TF2064 element overwritten
    // unread.
    public class FlowTests
    {
        [Test]
        public void ParameterOverwrittenBeforeUse_IsReported()
        {
            AnalyzerVerifier.Verify(new OverwrittenBeforeUseAnalyzer(), @"
using System;

class Target
{
    int Overwritten(int x) { {|TF2002:x|} = 5; return x; }
    int Conditional(int x, bool b) { if (b) {|TF2002:x|} = 3; return x; }
    int Self(int x) { {|TF2002:x|} = x; return x; }
    int Reads(int x) { x = x + 1; return x; }
    int Compound(int x) { x += 1; return x; }
    int AfterUse(int x) { Console.WriteLine(x); x = 9; return x; }
    void Out(out int x) { x = 1; }
    void Ref(ref int x) { x = 1; }
    string Coalesce(string s) { s ??= ""x""; return s; }

    void Catch()
    {
        try { }
        catch (Exception e) { {|TF2002:e|} = new Exception(); Console.WriteLine(e); }
    }

    void Lambda()
    {
        Func<int, int> f = y => { {|TF2002:y|} = 2; return y; };
        Func<int, int> g = y => y + 1;
    }

    int Captured(int x)
    {
        Func<int> read = () => x;
        x = 2;
        return read();
    }
}");
        }

        [Test]
        public void LoopEndingInAJump_IsReported()
        {
            AnalyzerVerifier.Verify(new LoopRunsOnceAnalyzer(), @"
using System;
using System.Collections.Generic;

class Target
{
    int Cases(List<int> list, bool b)
    {
        foreach (int v in list) { Console.WriteLine(v); {|TF2005:break;|} }
        for (int i = 0; i < 3; i++) {|TF2005:return i;|}
        while (b) { {|TF2005:throw new InvalidOperationException();|} }
        do { Console.WriteLine(); {|TF2005:break;|} } while (b);
        foreach (int v in list) { if (v > 1) break; {|TF2005:continue;|} }
        foreach (int v in list) { if (v > 1) continue; return v; }
        foreach (int v in list) { if (v > 1) break; Console.WriteLine(v); }
        foreach (int v in list) { if (v > 1) { return 1; } else { return 2; } }
        foreach (int v in list) { foreach (int w in list) { if (w > v) continue; } {|TF2005:return v;|} }
        return 0;
    }
}");
        }

        [Test]
        public void MemberThatNeverFinishes_IsReported()
        {
            AnalyzerVerifier.Verify(new NeverFinishesAnalyzer(), @"
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;

class Target
{
    int value;

    public int {|TF2016:Recursive|}(int n) => Recursive(n - 1);
    public int WithBase(int n) => n <= 0 ? 0 : WithBase(n - 1);
    public int {|TF2016:Health|} => Health;
    public int Backed { get { return value; } {|TF2016:set|} { Backed = value; } }
    public int Field { get => value; set => this.value = value; }

    public void {|TF2016:Spin|}() { while (true) { Console.WriteLine(); } }
    public void {|TF2016:ForEver|}() { for (;;) { Console.WriteLine(); } }
    public void {|TF2016:DoForever|}() { do { Console.WriteLine(); } while (true); }
    public void SpinWithBreak(bool b) { while (true) { if (b) break; } }
    public void SpinThrows() { while (true) { throw new InvalidOperationException(); } }
    public int SpinReturns() { while (true) { return 1; } }
    public void Variable() { bool running = true; while (running) { Console.WriteLine(); } }

    public IEnumerator Coroutine() { while (true) { yield return null; } }
    public async UniTask Poll(CancellationToken ct) { while (true) { await UniTask.Yield(ct); } }
    public void Other(Target other) => other.Other(this);
}");
        }

        [TestCase("public void Spin() { while (true) { System.Console.WriteLine(); } }", "'Spin' can never finish: no path leaves its loop")]
        [TestCase("public int Recursive(int n) => Recursive(n - 1);", "'Recursive' can never finish: every path calls it again")]
        public void Message_GivesTheNameAndReason(string member, string message)
        {
            Assert.That(AnalyzerVerifier.Messages(new NeverFinishesAnalyzer(), "class Target { " + member + " }"), Is.EqualTo(new[] { message }));
        }

        [Test]
        public void ReturningAVariableItsUsingDisposes_IsReported()
        {
            AnalyzerVerifier.Verify(new ReturnsDisposedObjectAnalyzer(), @"
using System.IO;

class Target
{
    Stream Block()
    {
        {|TF2039:using|} (MemoryStream stream = new MemoryStream())
        {
            return stream;
        }
    }

    Stream Declaration()
    {
        {|TF2039:using|} MemoryStream stream = new MemoryStream();
        return (stream);
    }

    long Fine()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            return stream.Length;
        }
    }

    Stream Other(Stream other)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            return other;
        }
    }

    Stream Lambda()
    {
        using (MemoryStream stream = new MemoryStream())
        {
            System.Func<Stream> later = () => stream;
            return null;
        }
    }
}");
        }

        [Test]
        public void ReturningAVariableItsUsingAssignsAndDisposes_IsReported()
        {
            AnalyzerVerifier.Verify(new ReturnsDisposedObjectAnalyzer(), @"
using System.IO;

class Target
{
    MemoryStream field;

    Stream Local(bool ready)
    {
        MemoryStream stream;
        {|TF2039:using|} (stream = new MemoryStream())
        {
            if (ready) return stream;
        }
        return null;
    }

    Stream Parameter(Stream stream, string path)
    {
        {|TF2039:using|} (stream = File.OpenRead(path))
        using (MemoryStream other = new MemoryStream())
        {
            return (stream);
        }
    }

    Stream Field()
    {
        {|TF2039:using|} (field = new MemoryStream())
        {
            return field;
        }
    }

    Stream ThisField()
    {
        using (this.field = new MemoryStream())
        {
            return this.field;
        }
    }

    Stream Bare(Stream stream)
    {
        using (stream)
        {
            return stream;
        }
    }

    Stream After()
    {
        MemoryStream stream;
        using (stream = new MemoryStream())
        {
            stream.WriteByte(1);
        }
        return stream;
    }
}");
        }

        [Test]
        public void FieldNothingSets_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldNeverAssignedAnalyzer(), @"
using System;
using UnityEngine;

class Player : MonoBehaviour
{
    private AudioSourceStub {|TF2054:source|};
    private int {|TF2054:lives|};
    private static string {|TF2054:prefix|};
    private readonly int[] {|TF2054:slots|};
    private int {|TF2054:Score|} { get; set; }
    private int assigned;
    private int initialized = 3;
    private int byRef;
    private int AssignedProperty { get; set; }
    private int unread;
    private Vector3 offset;
    [SerializeField] private int serialized;
    public int visible;

    void Start()
    {
        assigned = 1;
        AssignedProperty = 2;
        Bump(ref byRef);
        Debug.Log(source.Name + lives + prefix + slots.Length + Score + assigned + initialized + byRef + AssignedProperty
            + offset.x + serialized + visible);
    }

    static void Bump(ref int value) => value++;
}

class AudioSourceStub
{
    public string Name = string.Empty;
}

[Serializable]
class SaveData
{
    private int hidden;

    public int Read() => hidden;
}");
        }

        [Test]
        public void NestedTypeFieldsRefReturnsAndStructLayout_AreHandled()
        {
            AnalyzerVerifier.Verify(new FieldNeverAssignedAnalyzer(), @"
using System.Runtime.InteropServices;

class Handle
{
    public void Release() { }
}

class DebugHandler
{
    private Handle colorHandle;

    internal ref Handle ColorHandle => ref colorHandle;

    public void Dispose() => colorHandle?.Release();
}

class DeferredPass
{
    private class PassData
    {
        internal int[] {|TF2054:gbuffer|};
    }

    public int Count() => new PassData().gbuffer.Length;
}

[StructLayout(LayoutKind.Sequential)]
struct GuidParts
{
    private int a;
    private short b;

    public int Sum() => a + b;
}");
        }

        [Test]
        public void ElementSetTwiceWithoutARead_IsReported()
        {
            AnalyzerVerifier.Verify(new ElementOverwrittenUnreadAnalyzer(), @"
using System.Collections.Generic;

class Target
{
    void Cases(Dictionary<string, int> stats, List<int> list, int[] array, string key, HashSet<int> set)
    {
        stats[""hp""] = 10;
        stats[""mp""] = 5;
        {|TF2064:stats[""hp""] = 20;|}
        list[0] = 1;
        {|TF2064:list[0] = 2;|}
        stats.Add(key, 1);
        {|TF2064:stats[key] = 2;|}
        array[1] = 1;
        System.Console.WriteLine(array[1]);
        array[1] = 2;
        int i = 0;
        array[i] = 1;
        i++;
        array[i] = 2;
        stats[""xp""] = 1;
        stats[""xp""] = stats[""xp""] + 1;
        set.Add(1);
        set.Add(1);
    }
}");
        }

        [Test]
        public void KeysThatChangeSomething_NeverMatch()
        {
            AnalyzerVerifier.Verify(new ElementOverwrittenUnreadAnalyzer(), @"
using System.Collections.Generic;

class Target
{
    int next;

    int Key() => next++;

    void Cases(int[] buffer, Dictionary<int, int> map, int a, int b)
    {
        int i = 0;
        buffer[i++] = a;
        buffer[i++] = b;
        buffer[i] = a;
        buffer[--i] = b;
        buffer[i] = b;
        map[Key()] = 1;
        map[Key()] = 2;
        map.Add(Key(), 3);
        buffer[i = 2] = a;
        buffer[i = 2] = b;
        buffer[i] = a;
        buffer[i + 1] = b;
        {|TF2064:buffer[i] = b;|}
    }
}");
        }
    }
}