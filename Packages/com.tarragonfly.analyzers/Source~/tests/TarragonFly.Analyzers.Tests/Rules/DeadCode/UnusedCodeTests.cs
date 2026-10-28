using NUnit.Framework;
using TarragonFly.Analyzers.Rules.DeadCode;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.DeadCode
{
    // TF1500 unused private member, TF1505 StringBuilder never read, TF1506 private field never read.
    public class UnusedCodeTests
    {
        [Test]
        public void UnusedPrivateMembers_AreReported()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using System;

partial class Inventory
{
    {|TF1500:private int unusedField;|}
    {|TF1500:private int unusedA, unusedB;|}
    private int partlyUsed, {|TF1500:partlyUnused|};
    private int {|TF1500:UnusedProperty|} { get; set; }
    {|TF1500:private event Action UnusedEvent;|}
    private int used;

    private Inventory() { }

    private void {|TF1500:Helper|}() { }
    private void Recursive(int n) { if (n > 0) Recursive(n - 1); }
    private void NamedOnly() { }
    private void OnClick(object sender, EventArgs e) { }
    partial void {|TF1500:Hook|}();

    public string Name() => nameof(NamedOnly) + used + partlyUsed;

    public void Run()
    {
        int Local() => 1;
        int {|TF1500:Unused|}() => 2;
        Console.WriteLine(Local());
    }

    private class {|TF1500:UnusedNested|} { }

    private class UsedNested
    {
        {|TF1500:public static int Count;|}
        public void {|TF1500:Reset|}() { }
        public override string ToString() => string.Empty;
    }

    public object Make() => new UsedNested();
}

[Serializable]
class SaveData
{
    private int score;
    private void {|TF1500:Unused|}() { }
}");
        }

        [TestCase("private int currentScore, bestScore;", "currentScore")]
        [TestCase("private const int MinimumScore = 0, MaximumScore = 100;", "MinimumScore")]
        [TestCase("private event Action Reset, Changed;", "Reset")]
        public void UnusedFieldDeclarationMessage_NamesFirstDeclaredField(string declaration, string firstFieldName)
        {
            string[] messages = AnalyzerVerifier.Messages(new UnusedPrivateMemberAnalyzer(),
                "using System; class Scoreboard { " + declaration + " }");

            Assert.That(messages, Is.EqualTo(new[] { $"'{firstFieldName}' is never used; remove it" }));
        }

        [Test]
        public void UnityAndOdinUses_CountAsUsed()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting;

class Door : MonoBehaviour
{
    [SerializeField] private float speed;
    [ShowIf(""IsLocked"")] public int code;
    [ContextMenuItem(""Reset"", ""ResetCode"")] public int key;
    {|TF1500:private int unusedField;|}

    private void Update() { }
    private void OnValidate() { }
    private bool IsLocked() => true;
    private void ResetCode() { }
    [RuntimeInitializeOnLoadMethod] private static void Boot() { }
    [MenuItem(""Tools/Open"")] private static void Open() { }
    [ContextMenu(""Unlock"")] private void Unlock() { }
    [Preserve] private void Wired() { }
    [UsedImplicitly] private void FromUnityEvent() { }
    private void {|TF1500:Unused|}() { }
}");
        }

        [Test]
        public void EditorCallbacksAndNamesInStrings_CountAsUsed()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using UnityEditor;

class Importer : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) { }
}

class SaveHook : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths) => paths;
}

class Spawner
{
    private void Respawn() { }
    private void {|TF1500:Unnamed|}() { }

    public string Next() => ""Respawn"";
}");
        }

        [Test]
        public void EnumMembersAnimationEventsCallbacksAndHolders_CountAsUsed()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace UnityEngine
{
    public sealed class AnimationEvent { }
    public struct Rect { }
}

namespace UnityEditor
{
    public class EditorWindow : UnityEngine.ScriptableObject { }
    public class SettingsProvider { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SettingsProviderAttribute : Attribute { }
}

class Blitter
{
    private enum PassNames { Nearest, Bilinear }

    public string[] Passes() => Enum.GetNames(typeof(PassNames));
}

class Footsteps : MonoBehaviour
{
    private void OnFootstep(AnimationEvent animationEvent) { }
    private void {|TF1500:OnLand|}(int height) { }
    private void OnDidApplyAnimationProperties() { }
}

class LockableWindow : EditorWindow
{
    private void ShowButton(Rect position) { }
}

class TimelineLikeAsset : ScriptableObject
{
    private void __internalAwake() { }
}

class Palettes
{
    private class Importer : AssetPostprocessor { }

    private static class Registration
    {
        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider();
    }

    private class {|TF1500:Unused|} { }
}

[DebuggerDisplay(""{Label(),nq} {count}"")]
struct TextElement
{
    private int count;

    private string Label() => ""x"";
}");
        }

        [Test]
        public void GenericLocalFunctionsUsedThroughConstructedCopies_AreUsed()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using System;

public class Services
{
    public int Refresh()
    {
        static T[] FindAll<T>() where T : new() => new[] { new T() };
        void OnChanged<T>(T value) { }
        void {|TF1500:Unused|}<T>() { }

        Action<bool> changed = OnChanged;
        changed(true);
        return FindAll<object>().Length;
    }
}");
        }

        [Test]
        public void AccessorsNothingUses_AreReported()
        {
            AnalyzerVerifier.Verify(new UnusedPrivateMemberAnalyzer(), @"
using UnityEngine;

public class Hint
{
    public string Message { get; {|TF1500:private set;|} }
    public string Shown { get; private set; }
    public int Hidden { {|TF1500:private get;|} set; }
    [field: SerializeField] public int Level { get; private set; }

    private string texture;
    private string MainTexture { {|TF1500:get => texture;|} set => texture = value; }

    private int this[int index] { {|TF1500:get => index;|} set { } }
    private int this[string key] { get => 0; {|TF1500:set { }|} }
    private float {|TF1500:this|}[float key] => key;

    private class Result
    {
        public string Output { get; {|TF1500:set;|} }
        public int WrittenOnly { get; set; }
        public int Counter { get; set; }
        public int ByName { get; set; }
    }

    public string Read()
    {
        Result result = new Result { WrittenOnly = 1 };
        result.Counter++;
        Shown = nameof(Result.ByName);
        MainTexture = ""grass"";
        this[0] = 5;
        return result.Output + this[""a""] + texture;
    }
}");
        }

        [Test]
        public void StringBuilderFilledButNeverRead_IsReported()
        {
            const string Code = @"
using System.Text;

class Report
{
    string Build(bool b)
    {
        StringBuilder {|TF1505:unread = new StringBuilder()|};
        unread.Append(""x"").Append(""y"");
        var {|TF1505:cleared = new StringBuilder()|};
        cleared.AppendLine(""v"");
        cleared.Clear();
        StringBuilder {|TF1505:empty = new StringBuilder()|};
        StringBuilder read = new StringBuilder();
        read.Append(""a"");
        StringBuilder passed = new StringBuilder();
        Use(passed);
        StringBuilder measured = new StringBuilder();
        measured.Append(""q"");
        return read.ToString() + measured.Length;
    }

    static void Use(StringBuilder builder) { }
}";
            AnalyzerVerifier.Verify(new StringBuilderNeverReadAnalyzer(), Code);
            AnalyzerVerifier.Verify(new StringBuilderNeverReadAnalyzer(), Code.Replace("{|TF1505:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void FieldSetButNeverRead_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldNeverReadAnalyzer(), @"
using System;
using UnityEngine;

class Tracker : MonoBehaviour
{
    private float {|TF1506:lastHitTime|};
    private int {|TF1506:hits|};
    private int {|TF1506:combo|};
    private static int {|TF1506:total|};
    private int read;
    private int chained;
    private int initializedOnly = 3;
    private int counted;
    [SerializeField] private int serialized;
    public int visible;
    private event Action {|TF1506:Subscribed|};
    private event Action Raised;

    void Hit(float time)
    {
        lastHitTime = time;
        hits++;
        combo += 2;
        total = 1;
        read = 1;
        int both = chained = 2;
        int next = (counted += 1);
        serialized = 1;
        visible = 1;
        Subscribed += () => { };
        Raised?.Invoke();
        Debug.Log(read + both + next);
    }
}

[Serializable]
class SaveData
{
    private int score;

    public void Set(int x) => score = x;
}");
        }

        [Test]
        public void NestedTypeFieldsVoidArrowWritesAndStructLayout_AreHandled()
        {
            AnalyzerVerifier.Verify(new FieldNeverReadAnalyzer(), @"
using System.Runtime.InteropServices;

class SkyboxPass
{
    private float {|TF1506:lastHitTime|};

    private class PassData
    {
        internal int {|TF1506:material|};
        internal int camera;
    }

    public void Hit(float time) => lastHitTime = time;

    public int Record(int material, int camera)
    {
        PassData data = new PassData();
        data.material = material;
        data.camera = camera;
        return data.camera;
    }
}

[StructLayout(LayoutKind.Sequential)]
struct ListHeader
{
    public int length;
    private readonly int padding;

    public ListHeader(int value)
    {
        length = value;
        padding = 0;
    }
}");
        }
    }
}
