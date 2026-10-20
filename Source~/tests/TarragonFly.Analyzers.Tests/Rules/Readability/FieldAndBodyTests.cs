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
        public void FieldSetThroughConditionalsCoalescingOrInitializers_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldUsedOnlyAsLocalAnalyzer(), @"
class Holder { public string Name { get; set; } }

class Panel
{
    Holder {|TF1012:title|};
    Holder {|TF1012:footer|};
    int {|TF1012:count|};
    string {|TF1012:text|};
    int {|TF1012:parsed|};
    Holder lazy;
    int bumped;

    int Build(Holder given, bool flag, string s)
    {
        title = new Holder { Name = ""Title"" };
        footer = given ?? new Holder();
        count = flag ? 1 : 2;
        text = given?.Name;
        Parse(out parsed, s ?? """");
        lazy ??= new Holder();
        bumped += flag ? 1 : 2;
        return title.Name.Length + footer.Name.Length + count + text.Length + parsed + lazy.Name.Length + bumped;
    }

    static void Parse(out int value, string s) { value = s.Length; }
}");
        }

        [Test]
        public void FieldAnotherMemberMaySetBeforeTheRead_IsNotReported()
        {
            AnalyzerVerifier.Verify(new FieldUsedOnlyAsLocalAnalyzer(), @"
using System;

class Button { public event Action Clicked; }

class Counter
{
    int called;
    int throughDelegate;
    int throughProperty;
    int chained;
    int recursive;
    int {|TF1012:subscribed|};
    int {|TF1012:unrelated|};

    int Run()
    {
        called = 1;
        Reset();
        throughDelegate = 1;
        Action reset = ResetLater;
        reset();
        throughProperty = 1;
        int extra = Property;
        chained = 1;
        Outer();
        unrelated = 1;
        Other();
        return called + throughDelegate + throughProperty + chained + unrelated + extra;
    }

    void Reset() { called = 2; }
    void ResetLater() { throughDelegate = 2; }
    int Property { get { throughProperty = 2; return 0; } }
    void Outer() => Inner();
    void Inner() { chained = 2; }
    void Other() { }

    int Walk(int depth)
    {
        recursive = depth;
        if (depth > 0) Walk(depth - 1);
        return recursive;
    }

    Counter(Button button, int start)
    {
        subscribed = start;
        button.Clicked += OnClicked;
        Console.WriteLine(subscribed);
    }

    void OnClicked() { subscribed = 0; }
}");
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
        public void PointerFieldSetOnlyDuringConstruction_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldCouldBeReadonlyAnalyzer(), @"
unsafe class Buffer
{
    private int* {|TF1016:start|};
    private delegate*<int, void> {|TF1016:onWrite|};
    private int* cursor;

    public Buffer(int* memory, delegate*<int, void> callback)
    {
        start = memory;
        onWrite = callback;
        cursor = memory;
    }

    public void Advance() => cursor++;
}", new SnippetAssembly { AllowUnsafe = true });
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

        [Test]
        public void ReturnTypesConstraintsAndExplicitImplementations_AreCompared()
        {
            AnalyzerVerifier.Verify(new DuplicateMethodBodyAnalyzer(), @"
using System;

interface IInitializable<T>
{
    void Init(T argument);
}

class Client<TArgument> : IInitializable<TArgument>
{
    int state;

    void Run(TArgument argument) { }

    void IInitializable<TArgument>.Init(TArgument argument)
    {
        state = 1;
        Run(argument);
    }

    internal void {|TF1024:InitInternal|}(TArgument argument)
    {
        state = 1;
        Run(argument);
    }

    protected int Step(int token)
    {
        state++;
        return token;
    }

    protected long StepLong(int token)
    {
        state++;
        return token;
    }

    static bool SetEquatable<T>(ref T current, T next) where T : IEquatable<T>
    {
        if (current.Equals(next)) return false;
        current = next;
        return true;
    }

    static bool SetStruct<T>(ref T current, T next) where T : struct
    {
        if (current.Equals(next)) return false;
        current = next;
        return true;
    }
}");
        }
    }
}