using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1012 field used only as a local, TF1016 field that could be readonly, TF1024 methods with
    // identical bodies.
    public class FieldAndBodyTests
    {
        [Test]
        public void FieldAlwaysSetBeforeItIsRead_IsReported()
        {
            const string Code = @"
using System;
using UnityEngine;

class Panel
{
    int {|TF1012:temp|};
    int {|TF1012:sharedLocal|};
    static int {|TF1012:scratch|};
    int {|TF1012:parsed|};
    int {|TF1012:lambdaOnlyWrites|};
    int readFirst;
    int conditional;
    int acrossMethods;
    int withInitializer = 3;
    int compound;
    int lambdaRead;
    int onlyWritten;
    [SerializeField] int serialized;
    public int visible;

    int Bind(int x, bool b, string s)
    {
        temp = x * 2;
        if (b) temp++;
        sharedLocal = x;
        scratch = sharedLocal + temp;
        int.TryParse(s, out parsed);
        lambdaOnlyWrites = x;
        Action set = () => lambdaOnlyWrites = 5;
        int old = readFirst;
        readFirst = x;
        if (b) conditional = x;
        withInitializer = withInitializer + 1;
        compound += x;
        lambdaRead = x;
        Func<int> get = () => lambdaRead;
        onlyWritten = x;
        serialized = x;
        visible = x;
        return scratch + parsed + old + conditional + withInitializer + compound + lambdaOnlyWrites + serialized + visible + lambdaRead;
    }

    int Other(int x)
    {
        sharedLocal = x + 1;
        acrossMethods = x;
        return sharedLocal;
    }

    int ReadsAcross() => acrossMethods;
}";
            AnalyzerVerifier.Verify(new FieldUsedOnlyAsLocalAnalyzer(), Code);
            AnalyzerVerifier.Verify(new FieldUsedOnlyAsLocalAnalyzer(), Code.Replace("{|TF1012:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void PartialType_IsCheckedAcrossItsParts()
        {
            AnalyzerVerifier.Verify(new FieldUsedOnlyAsLocalAnalyzer(), @"
partial class Window
{
    int {|TF1012:tab|};
    int shared;

    int Build(int x)
    {
        tab = x;
        return tab;
    }
}

partial class Window
{
    int Read() => shared;
    void Write(int x) => shared = x;
}");
        }

        [Test]
        public void FieldSetOnlyDuringConstruction_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldCouldBeReadonlyAnalyzer(), @"
using System;
using System.Collections.Generic;
using UnityEngine;

class Settings
{
    int {|TF1016:fromConstructor|};
    int {|TF1016:fromInitializer|} = 3;
    List<int> {|TF1016:spawned|} = new List<int>();
    int {|TF1016:fromInit|};
    int changedLater;
    int neverSet;
    int inLambda;
    int byRef;
    static int shared = 1;
    public int visible = 1;
    [SerializeField] int serialized = 2;
    Vector3 offset = new Vector3();

    public int Init { get => 0; init { fromInit = value; } }

    Settings(int value)
    {
        fromConstructor = value;
        changedLater = value;
        Action set = () => inLambda = value;
    }

    void Change(int value)
    {
        changedLater = value;
        spawned.Add(value);
        Bump(ref byRef);
        offset.x = 1f;
    }

    static void Bump(ref int value) => value++;

    int Sum() => fromConstructor + fromInitializer + changedLater + neverSet + inLambda + byRef + shared + visible + serialized + fromInit + (int)offset.x;
}

struct Point
{
    int {|TF1016:x|};

    Point(int x) { this.x = x; }

    int X() => x;
}

class Other
{
    void Touch(Point p) { }
}");
        }

        [Test]
        public void MethodRepeatingAnEarlierBody_IsReported()
        {
            AnalyzerVerifier.Verify(new DuplicateMethodBodyAnalyzer(), @"
class Combo
{
    int total;

    void OnHit()
    {
        total = 0; // reset
        System.Console.WriteLine(total);
    }

    void {|TF1024:OnFall|}()
    {
        total  =  0;
        System.Console.WriteLine( total );
    }

    void Single() { total++; }
    void SingleAgain() { total++; }
    int Arrow() => total * 2;
    int ArrowAgain() => total * 2;

    void Add(int x)
    {
        total += x;
        System.Console.WriteLine(x);
    }

    void {|TF1024:AddAgain|}(int x)
    {
        total += x;
        System.Console.WriteLine(x);
    }

    void AddString(string x)
    {
        total += 1;
        System.Console.WriteLine(x);
    }

    void AddLong(long x)
    {
        total += 1;
        System.Console.WriteLine(x);
    }

    void WithLocal()
    {
        {|TF1024:Reset|}();

        void {|TF1024:Reset|}()
        {
            total = 0;
            System.Console.WriteLine(total);
        }
    }
}

class OtherType
{
    int total;

    void OnHit()
    {
        total = 0;
        System.Console.WriteLine(total);
    }
}".Replace("{|TF1024:Reset|}();", "Reset();"));
        }
    }
}