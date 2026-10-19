using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // The snippets hold the raw characters; the C# escapes below put them there. U+2028 and U+2029
    // end a line in C#, so they can't appear in a regular string at all.
    public class InvisibleCharacterInStringTests
    {
        [TestCase("\u00A0", TestName = "NonBreakingSpace")]
        [TestCase("\u200B", TestName = "ZeroWidthSpace")]
        [TestCase("\uFEFF", TestName = "ByteOrderMark")]
        [TestCase("\u000B", TestName = "VerticalTab")]
        [TestCase("\t", TestName = "Tab")]
        public void InvisibleCharacter_InARegularString_IsReported(string character)
        {
            AnalyzerVerifier.Verify(new InvisibleCharacterInStringAnalyzer(), "class Ids\n{\n    const string Scene = {|TF2030:\"intro" + character + "scene\"|};\n}");
        }

        [Test]
        public void InterpolatedText_IsReportedOnItsTextPart()
        {
            AnalyzerVerifier.Verify(new InvisibleCharacterInStringAnalyzer(), "class Ids\n{\n    string Name(int n) => $\"{|TF2030:panel\u00A0|}{n}\";\n}");
        }

        [Test]
        public void VerbatimStrings_AllowLineLayoutCharacters_ButNotOtherInvisibleOnes()
        {
            AnalyzerVerifier.Verify(new InvisibleCharacterInStringAnalyzer(),
                "class Ids\n{\n    const string Uss = @\"\n\t.panel { }\n\";\n    const string Id = {|TF2030:@\"intro\u00A0scene\"|};\n}");
        }

        [Test]
        public void EscapesAndCharacterLiterals_AreFine()
        {
            AnalyzerVerifier.Verify(new InvisibleCharacterInStringAnalyzer(), "class Ids\n{\n    const string Escaped = \"a\\u00A0b\\tc\";\n    const char Raw = '\u00A0';\n    const string Plain = \"intro scene\";\n}");
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new InvisibleCharacterInStringAnalyzer(), "class IdsTests\n{\n    const string Scene = \"intro\u00A0scene\";\n}", SnippetAssembly.Tests());
        }
    }
}