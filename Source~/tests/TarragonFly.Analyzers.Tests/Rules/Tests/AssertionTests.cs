using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Tests;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Tests
{
    // TF9002 test without an assertion, TF9003 literal bool in an assertion, TF9004 Thread.Sleep in a
    // test, TF9005 expected and actual swapped.
    public class AssertionTests
    {
        [Test]
        public void TestThatNeverChecksAResult_IsReported()
        {
            AnalyzerVerifier.Verify(new TestWithoutAssertionAnalyzer(), @"
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

class InventoryAssertions
{
    [Test]
    public void {|TF9002:AddsItem|}()
    {
        List<int> items = new List<int>();
        items.Add(1);
    }

    [TestCase(1)]
    public void {|TF9002:CallsPlainHelper|}(int x)
    {
        DoNothing();
    }

    [UnityTest]
    public IEnumerator {|TF9002:WaitsAFrame|}()
    {
        yield return null;
    }

    [Test]
    public void Asserts() => Assert.That(1, Is.EqualTo(1));

    [Test]
    public void ExpectsLog() => LogAssert.Expect(LogType.Error, ""x"");

    [Test]
    public void CallsCheckHelper() => CheckSomething();

    [Test]
    public void CallsHelperThatAsserts() => Outer();

    [Test]
    public void CallsOtherClass() => Helpers.Run();

    [Test]
    public void ThrowsAssertion() => throw new AssertionException(""x"");

    void DoNothing() { }
    void CheckSomething() { }
    void Outer() => Inner();
    void Inner() => Assert.IsTrue(true);
}

static class Helpers
{
    public static void Run() => Assert.Pass();
}", SnippetAssembly.Tests());
        }

        [Test]
        public void AssertionWords_OnlyCountAsWholeWords()
        {
            AnalyzerVerifier.Verify(new TestWithoutAssertionAnalyzer(), @"
using NUnit.Framework;

class Player
{
    public void Checkpoint() { }
    public int GetExpectedDamage() => 1;
    public void LogAssertion() { }
    public void AssertHealth() { }
    public void PreCheck() { }
    public void assert_value() { }
    public bool ShouldRespawn() => true;
}

class PlayerTests
{
    Player player = new Player();

    [Test]
    public void {|TF9002:ReachesCheckpoint|}() => player.Checkpoint();

    [Test]
    public void {|TF9002:ReadsDamage|}() => player.GetExpectedDamage();

    [Test]
    public void {|TF9002:Logs|}() => player.LogAssertion();

    [Test]
    public void Health() => player.AssertHealth();

    [Test]
    public void Pre() => player.PreCheck();

    [Test]
    public void Value() => player.assert_value();

    [Test]
    public void Respawns() => player.ShouldRespawn();
}", SnippetAssembly.Tests());
        }

        [Test]
        public void TestsCheckedThroughExpectedResultsDelegatesDynamicCallsOrThrows_AreNotReported()
        {
            AnalyzerVerifier.Verify(new TestWithoutAssertionAnalyzer(), @"
using System;
using System.Threading.Tasks;
using NUnit.Framework;

class ScoreTests
{
    static object[] Cases = { new object[] { 2 } };
    dynamic helper = null;

    [TestCase(2, ExpectedResult = 4)]
    public int Doubles(int value) => value * 2;

    [Test(ExpectedResult = 1)]
    public async Task<int> LoadsAsync()
    {
        await Task.Yield();
        return 1;
    }

    [TestCaseSource(nameof(Cases))]
    public int FromSource(int value) => value * 2;

    [Test]
    public int {|TF9002:ReturnsWithoutExpectedResult|}() => 1;

    [Test]
    public void CallsDynamic() => helper.Run();

    [Test]
    public void ThrowsStoredAssertion()
    {
        AssertionException failure = new AssertionException(""x"");
        throw failure;
    }

    [Test]
    public void CallsCheckDelegate()
    {
        Action check = () => { };
        check();
    }

    [Test]
    public void InvokesVerifyDelegate()
    {
        Action verify = () => { };
        verify.Invoke();
    }

    [Test]
    public void {|TF9002:CallsOtherDelegate|}()
    {
        Action run = () => { };
        run();
    }
}", SnippetAssembly.Tests());
        }

        [Test]
        public void LiteralBoolPassedToAssert_IsReported()
        {
            AnalyzerVerifier.Verify(new BoolLiteralInAssertionAnalyzer(), @"
using NUnit.Framework;

class Checks
{
    [Test]
    public void Cases()
    {
        bool open = true;
        {|TF9003:Assert.IsTrue(true)|};
        {|TF9003:Assert.AreEqual(true, open)|};
        {|TF9003:Assert.That(false, ""never"")|};
        Assert.That(open, Is.EqualTo(true));
        Assert.That(open, Is.True);
        Assert.IsTrue(open == true);
    }
}", SnippetAssembly.Tests());
        }

        [Test]
        public void ThreadSleepInsideATest_IsReported()
        {
            AnalyzerVerifier.Verify(new ThreadSleepInTestAnalyzer(), @"
using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

class Waits
{
    [Test]
    public void Sleeps()
    {
        {|TF9004:Thread.Sleep(10)|};
        Action wait = () => {|TF9004:Thread.Sleep(1)|};
    }

    [UnityTest]
    public IEnumerator SleepsInUnityTest()
    {
        {|TF9004:Thread.Sleep(TimeSpan.FromMilliseconds(1))|};
        yield return null;
    }

    [SetUp]
    public void Prepare() => Thread.Sleep(1);

    void Helper() => Thread.Sleep(1);
}", SnippetAssembly.Tests());
        }

        [Test]
        public void SleepImportedWithUsingStatic_IsReported()
        {
            AnalyzerVerifier.Verify(new ThreadSleepInTestAnalyzer(), @"
using NUnit.Framework;
using static System.Threading.Thread;

class Waits
{
    [Test]
    public void Sleeps() => {|TF9004:Sleep(1)|};
}

class Frames
{
    static void Sleep(int frames) { }

    [Test]
    public void SkipsFrames() => Sleep(1);
}", SnippetAssembly.Tests());
        }

        [Test]
        public void ConstantPassedAsActual_IsReported()
        {
            AnalyzerVerifier.Verify(new SwappedExpectedAndActualAnalyzer(), @"
using NUnit.Framework;

class Order
{
    const int Expected = 5;

    [Test]
    public void Cases()
    {
        int actual = Compute();
        Assert.AreEqual({|TF9005:actual, 5|});
        Assert.AreNotEqual({|TF9005:actual, Expected|});
        Assert.AreEqual({|TF9005:actual, 5.0|}, 0.1);
        Assert.That({|TF9005:5, Is.EqualTo(actual)|});
        Assert.That({|TF9005:5, Is.Not.EqualTo(actual)|});
        Assert.AreEqual(5, actual);
        Assert.That(actual, Is.EqualTo(5));
        Assert.AreEqual(""a"", ""b"");
        Assert.AreEqual(actual, Compute());
    }

    static int Compute() => 5;
}", SnippetAssembly.Tests());
        }

        [Test]
        public void NamedArguments_AreMatchedToTheirParameters()
        {
            AnalyzerVerifier.Verify(new SwappedExpectedAndActualAnalyzer(), @"
using NUnit.Framework;

class Order
{
    [Test]
    public void Cases()
    {
        int actual = Compute();
        bool done = actual > 0;
        Assert.AreEqual({|TF9005:expected: actual, actual: 5|});
        Assert.AreEqual({|TF9005:actual: 5, expected: actual|});
        Assert.AreEqual({|TF9005:actual, actual: 5|});
        Assert.AreEqual({|TF9005:expected: done, actual: true|}, message: ""m"");
        Assert.AreEqual(delta: 0.1, {|TF9005:actual: 5.0, expected: actual|});
        Assert.AreEqual(actual: actual, expected: 5);
        Assert.AreEqual(expected: 5, actual: actual);
        Assert.That(5, expression: Is.EqualTo(actual));
        Assert.That(expression: Is.EqualTo(actual), actual: 5);
    }

    static int Compute() => 5;
}", SnippetAssembly.Tests());
        }
    }
}