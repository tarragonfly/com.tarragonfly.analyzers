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
    }
}