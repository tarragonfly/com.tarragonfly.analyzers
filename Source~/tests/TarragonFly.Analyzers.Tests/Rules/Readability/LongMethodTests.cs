using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class LongMethodTests
    {
        private const string Settings = "max_method_lines = 3";

        [Test]
        public void BodyOverTheLimit_IsReportedOnTheName()
        {
            AnalyzerVerifier.Verify(new LongMethodAnalyzer(), @"
class Player
{
    int health;

    void {|TF1004:Update|}()
    {
        health++;
        health++;
        health++;
        health++;
    }

    {|TF1004:Player|}()
    {
        health = 1;
        health = 2;
        health = 3;
        health = 4;
    }
}", SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void BracesBlankLinesAndComments_DontCount()
        {
            AnalyzerVerifier.Verify(new LongMethodAnalyzer(), @"
class Player
{
    int health;

    void Update()
    {
        health++;

        // only three lines of code
        health++;
        health++;
    }
}", SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void LocalFunctions_CountTowardTheirMethod_AndAreNotReportedAlone()
        {
            AnalyzerVerifier.Verify(new LongMethodAnalyzer(), @"
class Player
{
    int health;

    void {|TF1004:Update|}()
    {
        Heal();

        void Heal()
        {
            health++;
            health++;
            health++;
            health++;
        }
    }
}", SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void ExpressionBody_CountsItsExpressionLines()
        {
            AnalyzerVerifier.Verify(new LongMethodAnalyzer(), @"
class Player
{
    int {|TF1004:Total|}() =>
        1 +
        2 +
        3 +
        4;
}", SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new LongMethodAnalyzer(), @"
class PlayerTests
{
    int health;

    void Heals()
    {
        health++;
        health++;
        health++;
        health++;
    }
}", SnippetAssembly.Tests(Settings));
        }
    }
}