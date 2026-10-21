using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1028 suppression without a reason.
    public class SuppressionWithoutReasonTests
    {
        // A bare "#pragma warning disable" switches off TF1028 itself from the next line on, so it
        // sits right before the bare restore. The spaces after CS0168 stay outside the reported span.
        private const string Pragmas = @"
class Player
{
{|TF1028:#pragma warning disable TF8100|}
{|TF1028:#pragma warning disable TF8100, CS0168|}" + "   " + @"
#pragma warning restore TF8100
{|TF1028:#pragma warning disable|}
#pragma warning restore
#pragma warning disable TF8101 // the command table is built once at startup
// the command table is built once at startup
#pragma warning disable TF8102
/* built once */
#pragma warning disable TF8103
    // indented comment
    #pragma warning disable TF8104
    /* a block comment
       over two lines */
#pragma warning disable TF8105
    /// <summary>a doc comment counts</summary>
#pragma warning disable TF8106
    // a blank line breaks the link

{|TF1028:#pragma warning disable TF8107|}
    int health; // a trailing comment on the line above counts
#pragma warning disable TF8108
}";

        private const string Attributes = @"
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using UnityEngine;

class Player
{
    const string Reason = ""the command table is built once"";

    [{|TF1028:SuppressMessage(""Performance"", ""TF8100"")|}]
    void A() { }

    [{|TF1028:SuppressMessage(""Performance"", ""TF8100"", Justification = """")|}]
    void B() { }

    [{|TF1028:SuppressMessage(""Performance"", ""TF8100"", Justification = ""  "")|}]
    void C() { }

    [SuppressMessage(""Performance"", ""TF8100"", Justification = ""the command table is built once"")]
    void D() { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(""Performance"", ""TF8100"", Justification = Reason)]
    void E() { }

    [{|TF1028:UsedImplicitly|}]
    void F() { }

    [UsedImplicitly] // called by the scenario runner through reflection
    void G() { }

    // called by the scenario runner through reflection
    [UsedImplicitly]
    void H() { }

    /// <summary>Called by Unity.</summary>
    [UsedImplicitly]
    void I() { }

    // a blank line breaks the link

    [{|TF1028:UsedImplicitly|}]
    void J() { }

    [SerializeField, {|TF1028:UsedImplicitly|}]
    int health;

    /* assigned from the Inspector */ [JetBrains.Annotations.UsedImplicitly]
    int armor;
}

namespace Other
{
    class SuppressMessageAttribute : System.Attribute
    {
        public SuppressMessageAttribute(string category, string id) { }
    }

    class UsedImplicitlyAttribute : System.Attribute { }

    class Tool
    {
        [SuppressMessage(""Performance"", ""TF8100"")] void A() { }
        [UsedImplicitly] void B() { }
    }
}";

        [Test]
        public void PragmaDisableWithoutAComment_IsReported()
        {
            AnalyzerVerifier.Verify(new SuppressionWithoutReasonAnalyzer(), Pragmas);
        }

        [Test]
        public void SuppressMessageWithoutJustification_AndUsedImplicitlyWithoutAComment_AreReported()
        {
            AnalyzerVerifier.Verify(new SuppressionWithoutReasonAnalyzer(), Attributes);
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new SuppressionWithoutReasonAnalyzer(), Pragmas, SnippetAssembly.Tests());
        }
    }
}
