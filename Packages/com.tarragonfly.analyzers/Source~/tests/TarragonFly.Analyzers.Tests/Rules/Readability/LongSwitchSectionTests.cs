using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class LongSwitchSectionTests
    {
        private const string Code = @"
class Enemy
{
    int state;

    void Think(System.Action[] actions)
    {
        switch (state)
        {
            {|TF1010:case 0:|}
            case 1:
                state++;
                state++;
                break;
            case 2:
                state++;
                break;
            {|TF1010:case 3:|}
                if (state > 0)
                {
                    state++;
                }
                break;
            {|TF1010:case 4:|}
                actions[0] = () => { state++; };
                break;
            case 5:
                {
                    state++;
                }
                break;
        }
    }
}";

        // Nested statements and those in lambdas count; the braces of blocks don't.
        [Test]
        public void SectionsOverTheLimit_AreReportedOnTheFirstLabel()
        {
            AnalyzerVerifier.Verify(new LongSwitchSectionAnalyzer(), Code, SnippetAssembly.WithSettings("max_switch_section_statements = 2"));
        }

        [Test]
        public void Message_GivesTheStatementCountAndLimit()
        {
            Assert.That(AnalyzerVerifier.Messages(new LongSwitchSectionAnalyzer(), @"
class Enemy
{
    int state;

    void Think()
    {
        switch (state)
        {
            case 0:
                state++;
                state++;
                break;
        }
    }
}", SnippetAssembly.WithSettings("max_switch_section_statements = 2")),
                Is.EqualTo(new[] { "This switch section has 3 statements, more than the limit of 2; move the work into a method" }));
        }

        [Test]
        public void SectionsUnderTheDefaultLimit_AreFine()
        {
            AnalyzerVerifier.Verify(new LongSwitchSectionAnalyzer(), Code.Replace("{|TF1010:", "").Replace(":|}", ":"));
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new LongSwitchSectionAnalyzer(), Code.Replace("{|TF1010:", "").Replace(":|}", ":"), SnippetAssembly.Tests("max_switch_section_statements = 2"));
        }
    }
}