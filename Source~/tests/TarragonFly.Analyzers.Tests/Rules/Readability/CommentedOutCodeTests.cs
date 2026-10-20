using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        [TestCase("{|TF1002:// if (health == 1) { health = 0; }|}")]
        [TestCase("{|TF1002://[field: SerializeField] public int Level { get; set; }|}")]
        [TestCase("{|TF1002:// Reset(); Play();|}")]
        [TestCase("{|TF1002://_array.GetArrayElementAtIndex(index));|}")]
        [TestCase("{|TF1002://, (GraphElement)nodeView);|}")]
        [TestCase("{|TF1002://     : 0f);|}")]
        [TestCase("{|TF1002://     ? one : two);|}")]
        [TestCase("{|TF1002://     && isDone);|}")]
        [TestCase("{|TF1002://     ?? fallback);|}")]
        [TestCase("{|TF1002://     .ToList());|}")]
        [TestCase("{|TF1002://     as Enemy);|}")]
        public void LinesEndingInABraceSeveralStatementsAndCallTails_AreReported(string comment)
        {
            Verify("    " + comment);
        }

        [TestCase("// Index Count;")]
        [TestCase("// this.speed;")]
        [TestCase("// speed * time * scale;")]
        [TestCase("// a + b + c > 0;")]
        [TestCase("// skipped porting: Reset();")]
        [TestCase("// a + b + c;")]
        [TestCase("// x is Enemy;")]
        [TestCase("// Use PreferFastBuild to avoid bug triggered with big meshes (UUM-52552));")]
        [TestCase("// see note (12));")]
        [TestCase("// : see the notes above);")]
        [TestCase("// && then it stops);")]
        [TestCase("// : it is done (mostly));")]
        public void ProseThatParses_IsNotReported(string comment)
        {
            Verify("    " + comment);
        }

        [TestCase("{|TF1002://Debug.Log(\"*** CalculatePreferredValues() ***\"); // ***** Frame: \" + Time.frameCount);|}")]
        [TestCase("{|TF1002://Debug.Log(\"***** Reset() *****\"); //has been called.\");|}")]
        [TestCase("{|TF1002:// Debug.Log(\"a\"); // note;|}")]
        [TestCase("// Debug.Log(\"a\"); // note")]
        [TestCase("// return; // early out")]
        public void CodeFollowedByAnotherComment_IsReportedWhenTheLineEndsLikeCode(string comment)
        {
            Verify("    " + comment);
        }

        [TestCase("{|TF1002:// catch { return false; }|}")]
        [TestCase("{|TF1002:// } else { health = 0; }|}")]
        [TestCase("{|TF1002:// finally { health = 0; }|}")]
        [TestCase("{|TF1002:// get { return health; }|}")]
        [TestCase("{|TF1002:// ?? Create(name);|}")]
        [TestCase("{|TF1002:// == null;|}")]
        [TestCase("{|TF1002:// is Enemy;|}")]
        [TestCase("{|TF1002:// / 2;|}")]
        [TestCase("{|TF1002://, MaskTex = 4 };|}")]
        [TestCase("{|TF1002:// , 4, 8 };|}")]
        [TestCase("{|TF1002:// , [2] = 5 };|}")]
        [TestCase("// else { something happens }")]
        [TestCase("// finally { done }")]
        [TestCase("// ?? see notes;")]
        [TestCase("// + more text here;")]
        [TestCase("// -a + b + c;")]
        [TestCase("// , and more };")]
        [TestCase("// , MaskTex = 4 }")]
        public void KeywordBlocksOperatorTailsAndListTails_AreReportedWhenTheyParse(string comment)
        {
            Verify("    " + comment);
        }

        [Test]
        public void KeywordBlockOperatorTailAndListTail_AfterCode_AreReported()
        {
            AnalyzerVerifier.Verify(new CommentedOutCodeAnalyzer(), @"
public enum MaskingTypes { MaskOff = 0, MaskHard = 1, MaskSoft = 2 }; {|TF1002://, MaskTex = 4 };|}

public class Settings
{
    private static object current;

    public static object Load(string name)
    {
        current = Find(name); {|TF1002:// ?? Create(name);|}
        return current;
    }

    public bool Clip(int value)
    {
        try
        {
            Find(value.ToString());
            return true;
        }
        {|TF1002://catch { return false; }|}
        finally
        {
            current = null;
        }
    }

    private static object Find(string name) => name;
    private static object Create(string name) => name;
}");
        }

        [TestCase("{|TF1002:// u2 - u;|}")]
        [TestCase("{|TF1002:// + offset;|}")]
        [TestCase("{|TF1002:// !flag;|}")]
        [TestCase("{|TF1002:// count;|}")]
        [TestCase("{|TF1002:// items[0];|}")]
        [TestCase("{|TF1002:// Settings.maxVisibleLights;|}")]
        [TestCase("{|TF1002:// desc.clearDepth;|}")]
        [TestCase("{|TF1002:// m_ShapeSegments[i].start;|}")]
        [TestCase("{|TF1002:// this.speed.Value;|}")]
        [TestCase("{|TF1002:// !taken && drinks.Count > 0;|}")]
        [TestCase("{|TF1002:// a > 0 && b > 0 && c > 0;|}")]
        [TestCase("{|TF1002:// GetDrinks().Count + bonus + 1;|}")]
        [TestCase("{|TF1002:// avoid (UUM-52552));|}")]
        public void ShortLeftoverExpressions_AreReported(string comment)
        {
            Verify("    " + comment);
        }

        [Test]
        public void LeftoverAfterAStatement_IsReported()
        {
            AnalyzerVerifier.Verify(new CommentedOutCodeAnalyzer(), @"class Mesh
{
    float offset;

    float Uv(int line)
    {
        float u = line % 1; {|TF1002:// + offset;|}
        return u;
    }

    public readonly bool clear; {|TF1002:// graph.Resources.Find(fragment.resource).clearBuffer;|}

    bool CanSit(bool taken, int[] drinks)
    {
        return true; {|TF1002://!taken && drinks.Length > 0;|}
    }
}");
        }

        [Test]
        public void CodeUnderACommentedOutCaseLabel_IsReported()
        {
            Verify(@"    // case 2:
    {|TF1002://     health = 3;|}
    //     break;");
        }

        [Test]
        public void HeaderWhoseConditionGoesOnOverLines_IsReported()
        {
            Verify(@"    // TODO: enable later
    {|TF1002:// if (health == 1 |||}
    //     health == 2)");
        }

        [Test]
        public void IfLinesBetweenLineComments_DoNotSplitTheComment()
        {
            Verify(@"//#if true
{|TF1002://    health = 1;|}
//#endif
#if true
//    health = 2;
#endif");
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

        // Unity compiles without parsing documentation, so /// lines arrive as plain comments.
        [Test]
        public void XmlDocumentation_IsNotChecked_WhenItArrivesAsPlainComments()
        {
            AnalyzerVerifier.Verify(new CommentedOutCodeAnalyzer(), @"class Toggle
{
    /// <example>
    /// <code>
    /// using UnityEngine;
    ///
    /// public class Example : MonoBehaviour
    /// {
    ///     void Start()
    ///     {
    ///         m_Toggle.onValueChanged.AddListener(delegate {
    ///                 ToggleValueChanged(m_Toggle);
    ///             });
    ///     }
    /// }
    ///</code>
    /// </example>
    int Value { get; set; }
}", new SnippetAssembly { ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9).WithDocumentationMode(DocumentationMode.None) });
        }
    }
}