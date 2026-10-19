using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Tests;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Tests
{
    // TF9000 ignored tests, TF9001 fixtures without tests, TF9006 tests the runner can't run.
    public class TestShapeTests
    {
        [Test]
        public void IgnoreWithoutAnEndDate_IsReported()
        {
            AnalyzerVerifier.Verify(new IgnoredTestAnalyzer(), @"
using NUnit.Framework;

[TestFixture]
class SaveTests
{
    [Test, {|TF9000:Ignore(""flaky on the build machine"")|}]
    public void Saves() { }

    [Test]
    [Ignore(""waiting on the API"", Until = ""2026-12-01"")]
    public void Loads() { }
}

[{|TF9000:Ignore(""parked"")|}]
class ParkedTests
{
    [Test]
    public void Inside() { }
}", SnippetAssembly.Tests());
        }

        [Test]
        public void Ignore_OutsideTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new IgnoredTestAnalyzer(), "class Tools { [NUnit.Framework.Ignore(\"x\")] public void Run() { } }", new SnippetAssembly { EditorOnly = true });
        }

        [Test]
        public void FixtureWithoutTests_IsReported()
        {
            AnalyzerVerifier.Verify(new FixtureWithoutTestsAnalyzer(), @"
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

[TestFixture]
class {|TF9001:EmptyTests|}
{
    public void NotATest() { }

    [SetUp]
    public void SetUp() { }
}

[TestFixture]
class UnityTests
{
    [UnityTest]
    public IEnumerator Plays() { yield return null; }
}

[TestFixture]
abstract class BaseTests
{
    [SetUp]
    public void SetUp() { }
}

[TestFixture]
class CaseTests
{
    [TestCase(1)]
    public void Works(int value) { }
}

class PlainTests
{
    [Test]
    public void Works() { }
}

[TestFixture]
class InheritedTests : PlainTests
{
}", SnippetAssembly.Tests());
        }

        [Test]
        public void TestShapesTheRunnerCantRun_AreReported()
        {
            AnalyzerVerifier.Verify(new UnrunnableTestAnalyzer(), @"
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

[TestFixture]
class SaveTests
{
    [Test]
    public async void {|TF9006:AsyncVoid|}() { await Task.Delay(1); }

    [Test]
    void {|TF9006:Private|}() { }

    [Test]
    public void {|TF9006:Generic|}<T>() { }

    [UnityTest]
    public void {|TF9006:UnityTestWithoutEnumerator|}() { }

    [Test]
    public IEnumerator {|TF9006:EnumeratorWithoutUnityTest|}() { yield return null; }

    [Test]
    public int {|TF9006:ReturnsValue|}() => 1;

    [Test]
    public async Task AsyncTask() { await Task.Delay(1); }

    [TestCase(1)]
    public void GenericWithCase<T>(T value) { }

    [TestCase(2, ExpectedResult = 4)]
    public int Doubles(int value) => value * 2;

    [UnityTest]
    public IEnumerator Plays() { yield return null; }

    [Test]
    public static void Static() { }
}", SnippetAssembly.Tests());
        }
    }
}