using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class CommentedOutCodeTests
    {
        private static void Verify(string body)
        {
            AnalyzerVerifier.Verify(new CommentedOutCodeAnalyzer(), "class Player\n{\n    int health;\n\n" + body + "\n    void Run() { }\n}");
        }

        [TestCase("{|TF1002:// health -= 10;|}")]
        [TestCase("{|TF1002://health = 0;|}")]
        [TestCase("{|TF1002:// private int armor;|}")]
        [TestCase("{|TF1002:// using System.Linq;|}")]
        [TestCase("{|TF1002:// if (health < 0)|}")]
        [TestCase("{|TF1002:// foreach (var enemy in enemies)|}")]
        [TestCase("{|TF1002:// } else { |}")]
        [TestCase("{|TF1002:// }|}")]
        [TestCase("{|TF1002:// );|}")]
        [TestCase("{|TF1002:/* Debug.Log(health); */|}")]
        public void CodeLine_IsReported(string comment)
        {
            Verify("    " + comment);
        }

        [TestCase("// Plain prose about the damage formula.")]
        [TestCase("// One sentence; another sentence;")]
        [TestCase("// Note: armor applies first; shield second")]
        [TestCase("// Values: { 1, 2, 3 }")]
        [TestCase("// Ends in a brace like this }")]
        [TestCase("// int x = 5")]
        [TestCase("// [SerializeField]")]
        [TestCase("// See https://example.com/a;b")]
        [TestCase("// TODO remove(); later")]
        [TestCase("// Example: player.Restore(10);")]
        public void Prose_IsNotReported(string comment)
        {
            Verify("    " + comment);
        }

        [Test]
        public void ConsecutiveLineComments_AreOneWarningAtTheFirstCodeLine()
        {
            Verify(@"    // else
    {|TF1002:// { |}
    //     health = 1;

    // }");
        }

        [Test]
        public void BlockComment_StandsAlone_AndSplitsLineComments()
        {
            Verify(@"    {|TF1002:// health = 1;|}
    // health = 2;
    {|TF1002:/* health = 3; */|}
    {|TF1002:// health = 4;|}");
        }

        // The usage example from DialogueTextPrinter.cs.
        [Test]
        public void UsageExample_AfterAColonLine_IsNotReported()
        {
            Verify(@"    // Drives a typewriter reveal on a label.
    //
    // Usage:
    //   var printer = new DialogueTextPrinter();
    //   printer.BeginPrinting(label, ""Hello world."", speed);
    //   await printer.WaitUntilFinishedAsync(ct);");
        }

        [Test]
        public void CodeBeforeTheExampleLine_IsStillReported()
        {
            Verify(@"    {|TF1002:// var x = new Player();|}
    // Use it like this:
    //   x.Run();");
        }

        [Test]
        public void InsideAMultiLineBlockComment_TheWholeLineIsReported()
        {
            AnalyzerVerifier.Verify(new CommentedOutCodeAnalyzer(), "class Player\n{\n    /*\n{|TF1002:     * int old = health;|}\n     * health = old;\n     */\n    int health;\n}");
        }

        [Test]
        public void TrailingCommentAfterCode_IsChecked()
        {
            Verify("    int armor = 1; {|TF1002:// armor = 2;|}");
        }

        [Test]
        public void XmlDocumentation_IsNotChecked()
        {
            Verify(@"    /// <summary>Restores health.</summary>
    /// <example>
    /// <code>
    /// player.Restore(10);
    /// </code>
    /// </example>");
        }
    }
}