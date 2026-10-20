using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Performance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Performance
{
    // TF8001 empty methods, TF8007 Equals(T) without IEquatable<T>.
    public class EmptyAndEquatableTests
    {
        private const string EmptyMembers = @"
using System;
using UnityEngine;

interface IRunner
{
    void Run();
}

abstract class Base
{
    public virtual void Virtual() { }
    public abstract void Abstract();
    public virtual int Value { get; set; }
}

class Empty : Base, IRunner
{
    public Empty() { }
    ~Empty() { }

    public void {|TF8001:Plain|}() { }
    public static void {|TF8001:Static|}() { }
    public void Commented()
    {
        // waiting on the save system
    }
    public void Run() { }
    public override void {|TF8001:Virtual|}() { }
    public override void Abstract() { }
    public override int Value { get => 0; {|TF8001:set|} { } }
    public int Health { get { return 0; } {|TF8001:set|} { } }

    public void WithLocal()
    {
        void {|TF8001:Local|}() { }
        Action lambda = () => { };
        Local();
    }
}

class Player : MonoBehaviour
{
    void {|TF8001:Update|}() { }
}";

        [Test]
        public void EmptyMembers_AreReported()
        {
            AnalyzerVerifier.Verify(new EmptyMethodAnalyzer(), EmptyMembers);
        }

        [Test]
        public void EmptyUnityMessage_NamesTheMessage()
        {
            Microsoft.CodeAnalysis.Diagnostic diagnostic = null;
            foreach (Microsoft.CodeAnalysis.Diagnostic reported in AnalyzerVerifier.Run(new EmptyMethodAnalyzer(), "class Player : UnityEngine.MonoBehaviour { void Update() { } }"))
            {
                diagnostic = reported;
            }
            Assert.That(diagnostic?.GetMessage(), Does.Contain("empty Update message"));
        }

        // Test base classes often have empty hooks that fixtures override and leave empty.
        [Test]
        public void EmptyOverrides_InTestCode_AreSkipped()
        {
            AnalyzerVerifier.Verify(new EmptyMethodAnalyzer(), @"
class FixtureBase
{
    public virtual void Prepare() { System.Console.WriteLine(); }
}

class Fixture : FixtureBase
{
    public override void Prepare() { }
    public void {|TF8001:Helper|}() { }
}", SnippetAssembly.Tests());
        }

        [Test]
        public void EmptyGetResultOfAnAwaiter_IsFine()
        {
            AnalyzerVerifier.Verify(new EmptyMethodAnalyzer(), @"
using System;
using System.Runtime.CompilerServices;

struct Awaiter : ICriticalNotifyCompletion
{
    public bool IsCompleted => true;
    public void GetResult() { }
    public void OnCompleted(Action continuation) => continuation();
    public void UnsafeOnCompleted(Action continuation) => continuation();
    public void {|TF8001:Reset|}() { }
}

class NotAnAwaiter
{
    public void {|TF8001:GetResult|}() { }
}");
        }

        [Test]
        public void EqualsWithoutIEquatable_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new EquatableMissingAnalyzer(), @"
using System;

class {|TF8007:Cell|}
{
    public bool Equals(Cell other) => other != null;
}

struct {|TF8007:Point|}
{
    public bool Equals(Point other) => true;
}

class Implemented : IEquatable<Implemented>
{
    public bool Equals(Implemented other) => other != null;
}

class Base : IEquatable<Base>
{
    public bool Equals(Base other) => other != null;
}

class {|TF8007:Derived|} : Base
{
    public bool Equals(Derived other) => other != null;
}

class OtherType
{
    public bool Equals(string other) => other != null;
}

class StaticEquals
{
    public static bool Equals(StaticEquals other) => other != null;
}");
        }
    }
}