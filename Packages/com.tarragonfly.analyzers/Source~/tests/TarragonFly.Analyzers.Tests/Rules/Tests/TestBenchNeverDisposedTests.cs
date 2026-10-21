using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Tests;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Tests
{
    // TF9007 TestBench started but never disposed.
    public class TestBenchNeverDisposedTests
    {
        private const string Code = @"
using System.Collections;
using NUnit.Framework;
using TarragonFly.TestBench;
using UnityEngine.TestTools;

class BoardTests
{
    TestBench _bench;

    [SetUp]
    public void SetUp() => TestBench.Start(out _bench);

    [TearDown]
    public void TearDown() => _bench.Dispose();

    [Test]
    public void Spawns() { }
}

class ForgottenTests
{
    TestBench _bench;
    TestBench _other;

    [SetUp]
    public void SetUp()
    {
        {|TF9007:TestBench.Start(out _bench)|};
        {|TF9007:TestBench.Start(new object(), out _other)|}.Under(this);
        {|TF9007:TestBench.Start(out _)|};
        {|TF9007:TestBench.Start(out var _)|};
        {|TF9007:TestBench.Start(out TestBench lost)|};
        TestBench.Start(out TestBench disposed);
        disposed.Dispose();
    }

    [Test]
    public void Spawns() => _bench.Dispose();
}

abstract class BenchTests
{
    protected TestBench Bench;

    [TearDown]
    public void TearDown() => CleanUp();

    void CleanUp()
    {
        Bench?.Dispose();
    }
}

class DerivedTests : BenchTests
{
    [SetUp]
    public void SetUp() => TestBench.Start(out Bench);

    [Test]
    public void Runs() { }
}

class PlayModeTests
{
    TestBench _bench;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        TestBench.Start(out _bench);
        yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        this._bench.Dispose();
        yield break;
    }

    [UnityTest]
    public IEnumerator Runs() { yield break; }
}

class SharedBenchTests
{
    TestBench _bench;

    [OneTimeSetUp]
    public void SetUp() => TestBench.Start(out _bench);

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_bench != null) _bench.Dispose();
    }

    [Test]
    public void Runs() { }
}";

        [Test]
        public void BenchWithoutATearDownDispose_IsReported()
        {
            AnalyzerVerifier.Verify(new TestBenchNeverDisposedAnalyzer(), Code, SnippetAssembly.Tests());
        }

        [Test]
        public void OutsideTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new TestBenchNeverDisposedAnalyzer(), Markup.Without(Code, "TF9007"), new SnippetAssembly { EditorOnly = true });
        }

        // The package is found by name, so another Start(out bench) isn't the rule's business.
        [Test]
        public void OtherTestBenchTypes_AreNotChecked()
        {
            AnalyzerVerifier.Verify(new TestBenchNeverDisposedAnalyzer(), @"
using NUnit.Framework;

class TestBench : System.IDisposable
{
    public static TestBench Start(out TestBench bench) => bench = new TestBench();
    public void Dispose() { }
}

class RigTests
{
    TestBench _bench;

    [SetUp]
    public void SetUp() => TestBench.Start(out _bench);
}", SnippetAssembly.Tests());
        }

        [Test]
        public void Message_NamesTheField()
        {
            string[] messages = AnalyzerVerifier.Messages(new TestBenchNeverDisposedAnalyzer(), @"
using NUnit.Framework;
using TarragonFly.TestBench;

class BoardTests
{
    TestBench _bench;

    [SetUp]
    public void SetUp()
    {
        TestBench.Start(out _bench);
        TestBench.Start(out _);
    }
}", SnippetAssembly.Tests());

            Assert.That(messages, Is.EqualTo(new[]
            {
                "'_bench' is started but nothing disposes it; add a TearDown that calls Dispose()",
                "'TestBench' is started but nothing disposes it; add a TearDown that calls Dispose()"
            }));
        }
    }
}
