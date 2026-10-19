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
    }
}