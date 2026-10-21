using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1027 non-ASCII character in source. The snippets hold the raw characters; the C# escapes
    // below put them there, so this file itself stays ASCII.
    public class NonAsciiCharacterTests
    {
        private const string Code =
            "class Ids\n" +
            "{\n" +
            "    // wait {|TF1027:\u2026|} here \u2026 and again\n" +
            "    const string Name = \"caf{|TF1027:\u00E9|}\";\n" +
            "    const char Letter = '{|TF1027:\u00F1|}';\n" +
            "    string Text(int n) => $\"{|TF1027:\u00FC|}{n}\";\n" +
            "    int na{|TF1027:\u00EF|}ve;\n" +
            "{|TF1027:\u00A0|}    int indented;\n" +
            "    /* smile {|TF1027:\uD83D\uDE00|} */\n" +
            "    const string Escaped = \"caf\\u00E9 \\u2026\";\n" +
            "}\n";

        [Test]
        public void WithAsciiOnly_TheFirstCharacterOnEachLine_IsReported()
        {
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Code, SnippetAssembly.WithSettings("ascii_only = true"));
        }

        [Test]
        public void Message_CarriesTheCodePoint_AlsoForASurrogatePair()
        {
            string[] messages = AnalyzerVerifier.Messages(new NonAsciiCharacterAnalyzer(), Markup.Without(Code, "TF1027"), SnippetAssembly.WithSettings("ascii_only = true"));

            Assert.That(messages[0], Is.EqualTo("Line contains a non-ASCII character (U+2026); write it as an escape or replace it"));
            Assert.That(messages[6], Is.EqualTo("Line contains a non-ASCII character (U+1F600); write it as an escape or replace it"));
        }

        [Test]
        public void WithoutTheSetting_NothingIsReported()
        {
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Markup.Without(Code, "TF1027"));
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Markup.Without(Code, "TF1027"), SnippetAssembly.WithSettings("ascii_only = false"));
        }

        [TestCase("Assets/Game/Dialogue/*")]
        [TestCase("Assets\\Game\\Dialogue\\*")]
        [TestCase("*/Dialogue/*, Assets/Banners/*")]
        public void FilesMatchingAnAllowedPath_AreLeftAlone(string allowed)
        {
            string settings = "ascii_only = true\nnon_ascii_allowed_in = " + allowed;
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Markup.Without(Code, "TF1027"), new SnippetAssembly { FilePath = "Assets/Game/Dialogue/Intro.cs", Settings = settings });
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Code, new SnippetAssembly { FilePath = "Assets/Game/Intro.cs", Settings = settings });
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new NonAsciiCharacterAnalyzer(), Code, SnippetAssembly.Tests("ascii_only = true"));
        }
    }
}
