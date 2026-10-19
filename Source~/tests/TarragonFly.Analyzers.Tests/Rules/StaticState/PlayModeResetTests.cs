using NUnit.Framework;
using TarragonFly.Analyzers.Rules.StaticState;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.StaticState
{
    // TF7100 static value not reset when Play Mode starts, TF7101 static event not cleared.
    public class PlayModeResetTests
    {
        private const string Code = @"
using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

class Score
{
    static int {|TF7100:total|};
    static int resetTotal;
    static int lateTotal;
    static int editorTotal;
    public static bool {|TF7100:DebugMode|};
    static Score {|TF7100:Last|} { get; set; }
    static int Lookup { get; } = 3;
    static readonly int[] Table = { 1, 2 };
    static int setOnce = 5;
    static int setInConstructor;
    const int Max = 10;
    [ThreadStatic] static string parsingLine;

    static Action {|TF7101:Changed|};
    public static event Action<int> {|TF7101:ScoreChanged|};
    static event Action Reset;
    static event Action Cleared;
    static Action unused;

    static Score()
    {
        setInConstructor = 1;
    }

    void Add(int points)
    {
        total += points;
        resetTotal += points;
        lateTotal++;
        editorTotal = points;
        parsingLine = null;
        Last = this;
        Changed += OnChanged;
        Reset += OnChanged;
        Cleared += OnChanged;
        Console.WriteLine(setOnce + setInConstructor + Lookup + Table[0] + Max);
    }

    void OnChanged() { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        resetTotal = 0;
        Cleared = null;
    }

    [RuntimeInitializeOnLoadMethod]
    static void TooLate()
    {
        lateTotal = 0;
        Reset = null;
    }

    [UnityEditor.InitializeOnEnterPlayMode]
    static void OnEnterPlayMode()
    {
        editorTotal = 0;
    }
}

[AutoStaticsCleanup]
class CleanedByUnity
{
    static int count;
    static event Action Changed;

    void Add()
    {
        count++;
        Changed += Add;
    }
}

class Kept
{
    [NoAutoStaticsCleanup] static int sessions;

    void Start() => sessions++;
}";

        [Test]
        public void StaticValueNeverReset_IsReported()
        {
            AnalyzerVerifier.Verify(new StaticFieldNotResetAnalyzer(), Markup.Without(Code, "TF7101").Replace("static int lateTotal;", "static int {|TF7100:lateTotal|};"));
        }

        [Test]
        public void StaticEventNeverCleared_IsReported()
        {
            AnalyzerVerifier.Verify(new StaticEventNotClearedAnalyzer(), Markup.Without(Code, "TF7100").Replace("static event Action Reset;", "static event Action {|TF7101:Reset|};"));
        }

        [Test]
        public void EditorCode_IsNotChecked()
        {
            SnippetAssembly editor = new SnippetAssembly { AssemblyName = "Game.Editor", EditorOnly = true };
            string plain = Markup.Without(Markup.Without(Code, "TF7100"), "TF7101");
            AnalyzerVerifier.Verify(new StaticFieldNotResetAnalyzer(), plain, editor);
            AnalyzerVerifier.Verify(new StaticEventNotClearedAnalyzer(), plain, editor);
        }
    }
}