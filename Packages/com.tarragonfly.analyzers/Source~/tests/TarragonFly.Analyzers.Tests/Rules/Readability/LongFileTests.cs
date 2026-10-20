using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class LongFileTests
    {
        [Test]
        public void FileOverTheLimit_IsReportedAtItsStart()
        {
            AnalyzerVerifier.Verify(new LongFileAnalyzer(), @"{|TF1000:|}class Player
{
    int health;
}", SnippetAssembly.WithSettings("max_file_lines = 3"));
        }

        [Test]
        public void BlankAndCommentLines_DontCount()
        {
            AnalyzerVerifier.Verify(new LongFileAnalyzer(), @"class Player
{

    // health is set in Awake
    /* and saved
       on quit */
#if UNITY_EDITOR
#endif
}", SnippetAssembly.WithSettings("max_file_lines = 3"));
        }

        [Test]
        public void ShortFile_WithTheDefaultLimit_IsFine()
        {
            AnalyzerVerifier.Verify(new LongFileAnalyzer(), "class Player { int health; }");
        }

        [Test]
        public void Message_GivesTheLineCountAndLimit()
        {
            Assert.That(AnalyzerVerifier.Messages(new LongFileAnalyzer(), "class Player\n{\n    int health;\n}", SnippetAssembly.WithSettings("max_file_lines = 3")),
                Is.EqualTo(new[] { "This file has 4 lines of code, more than the limit of 3; split it by responsibility" }));
        }
    }
}