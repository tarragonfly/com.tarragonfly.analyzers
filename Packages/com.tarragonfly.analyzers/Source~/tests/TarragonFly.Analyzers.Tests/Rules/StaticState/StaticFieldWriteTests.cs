using NUnit.Framework;
using TarragonFly.Analyzers.Rules.StaticState;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.StaticState
{
    // TF7002 instance member writing a static field, TF7004 constructor writing a static field,
    // TF7005 static field read before it is set.
    public class StaticFieldWriteTests
    {
        [Test]
        public void FirstStaticWritePerInstanceMember_IsReported()
        {
            const string Code = @"
using UnityEngine;

class Spawner : MonoBehaviour
{
    static Spawner instance;
    static int spawned;
    static int[] slots = new int[1];
    static int Total { get; set; }

    void Awake()
    {
        {|TF7002:instance =|} this;
        {|TF7002:spawned|}++;
        spawned = 5;
        Spawner.spawned += 1;
        slots[0] = 1;
        Total = 3;
        System.Threading.Interlocked.Increment(ref spawned);
        UnityEditor.EditorApplication.delayCall += Later;
    }

    int Count
    {
        set => {|TF7002:Spawner.spawned +=|} value;
    }

    void Later()
    {
        System.Action reset = () => {|TF7002:spawned =|} 0;
    }

    static void Reset()
    {
        spawned = 0;
    }

    Spawner()
    {
        spawned = 1;
    }
}";
            AnalyzerVerifier.Verify(new InstanceWritesStaticFieldAnalyzer(), Code);
            AnalyzerVerifier.Verify(new InstanceWritesStaticFieldAnalyzer(), Code.Replace("{|TF7002:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void StaticWriteInInstanceConstructor_IsReported()
        {
            AnalyzerVerifier.Verify(new ConstructorWritesStaticFieldAnalyzer(), @"
class Enemy
{
    static int alive;
    static Enemy last;
    int own;

    static Enemy()
    {
        alive = 0;
    }

    Enemy()
    {
        {|TF7004:alive|}++;
        alive = 2;
        {|TF7004:last =|} this;
        own = 1;
    }

    Enemy(int start) : this()
    {
        {|TF7004:alive +=|} start;
    }

    void Method()
    {
        alive = 2;
    }
}");
        }

        [Test]
        public void StaticFieldOfAnAssemblyUnityBuildsFromScripts_IsReported()
        {
            SnippetAssembly assembly = new SnippetAssembly();
            assembly.ReferencedAssemblies["Library/Bee/artifacts/1900b0aE.dag/Game.Core.ref.dll"] = "namespace Core { public static class Counters { public static int Spawned; } }";
            assembly.ReferencedAssemblies["C:/Game/Library/ScriptAssemblies/Game.Data.dll"] = "namespace Data { public static class Registry { public static object Current; } }";
            assembly.ReferencedAssemblies["C:\\Game\\Logs\\analyzers\\build/Game.Audio.ref.dll"] = "namespace Audio { public static class Mixer { public static float Volume; } }";
            assembly.ReferencedAssemblies["Assets/Plugins/Tweens.dll"] = "namespace Tweens { public static class Defaults { public static float Duration; } }";
            assembly.ReferencedAssemblies["Library/PackageCache/com.example.tools@1.0.0/Plugins/Tools.dll"] = "namespace Tools { public static class Settings { public static int Level; } }";
            assembly.ReferencedAssemblies["C:/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Data/Managed/UnityEngine/UnityEngine.Extra.dll"] = "namespace Engine { public static class Hooks { public static int Count; } }";

            AnalyzerVerifier.Verify(new InstanceWritesStaticFieldAnalyzer(), @"
class Spawner
{
    void Spawn()
    {
        {|TF7002:Core.Counters.Spawned|}++;
        {|TF7002:Data.Registry.Current =|} this;
        {|TF7002:Audio.Mixer.Volume =|} 0.5f;
        Tweens.Defaults.Duration = 1f;
        Tools.Settings.Level = 2;
        Engine.Hooks.Count = 3;
    }
}", assembly);
            AnalyzerVerifier.Verify(new ConstructorWritesStaticFieldAnalyzer(), @"
class Spawner
{
    Spawner()
    {
        {|TF7004:Core.Counters.Spawned|}++;
        Tweens.Defaults.Duration = 1f;
    }
}", assembly);
        }

        [Test]
        public void StaticInitializerReadingALaterField_IsReported()
        {
            AnalyzerVerifier.Verify(new StaticFieldReadBeforeSetAnalyzer(), @"
using System;

class Stats
{
    static readonly int MaxHealth {|TF7005:= BaseHealth * 2|};
    static readonly int BaseHealth = 50;
    static int first {|TF7005:= second + 1|}, second = 3;
    static string label = nameof(count);
    static int count = 1;
    static readonly Func<int> Later = () => Last;
    static readonly int Last = 4;
    const int Doubled = Single * 2;
    const int Single = 1;
    static readonly int AfterBase = BaseHealth + 1;
    int instance = Last;
}");
        }

        [Test]
        public void QueryClausesThatBecomeLambdas_ReadLater()
        {
            AnalyzerVerifier.Verify(new StaticFieldReadBeforeSetAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Queries
{
    static readonly int[] Source = { 1, 2 };
    static readonly IEnumerable<int> Filtered = from x in Source where x > Threshold select x + Offset;
    static readonly IEnumerable<int> Nested = from x in Source from y in Extra select x + y;
    static readonly IEnumerable<int> Early {|TF7005:= from x in Late select x|};
    static readonly IEnumerable<int> Joined {|TF7005:= from x in Source join y in Late on x equals y select y|};
    static readonly int Threshold = 1;
    static readonly int Offset = 2;
    static readonly int[] Extra = { 3 };
    static readonly int[] Late = { 4 };
}");
        }
    }
}