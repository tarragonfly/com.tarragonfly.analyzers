using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Settings;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1029 direct call where the project has a helper.
    public class DiscouragedMemberCallTests
    {
        private const string Views = @"
using UnityEngine.UIElements;

namespace Game.UI
{
    class UIViewBase
    {
        protected VisualElement Root;
        protected T Element<T>(string name) where T : VisualElement => Root.Q<T>(name);

        class Lookup
        {
            VisualElement Find(VisualElement root) => root.Q(""panel"");
        }
    }

    class MainMenuView : UIViewBase
    {
        void Bind()
        {
            Button play = Root.{|TF1029:Q|}<Button>(""play"");
            VisualElement panel = Root.{|TF1029:Q|}(""panel"");
            VisualElement byClass = UQueryExtensions.{|TF1029:Q|}(Root, null, ""panel"");
            Button quit = Element<Button>(""quit"");
        }
    }
}";

        [Test]
        public void ListedMember_IsReportedOutsideTheAllowedTypes()
        {
            AnalyzerVerifier.Verify(new DiscouragedMemberCallAnalyzer(), Views, SnippetAssembly.WithSettings(
                "discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element\n" +
                "discouraged_members_allowed_in = UIViewBase\n"));
            AnalyzerVerifier.Verify(new DiscouragedMemberCallAnalyzer(), Views, SnippetAssembly.WithSettings(
                "discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element\n" +
                "discouraged_members_allowed_in = Game.UI.UIViewBase\n"));
        }

        [Test]
        public void WithoutTheSetting_NothingIsReported()
        {
            AnalyzerVerifier.Verify(new DiscouragedMemberCallAnalyzer(), Markup.Without(Views, "TF1029"));
        }

        [Test]
        public void Message_NamesBothMembers()
        {
            string[] messages = AnalyzerVerifier.Messages(new DiscouragedMemberCallAnalyzer(), Markup.Without(Views, "TF1029"), SnippetAssembly.WithSettings(
                "discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element\n" +
                "discouraged_members_allowed_in = UIViewBase\n"));

            Assert.That(messages, Has.Length.EqualTo(3));
            Assert.That(messages[0], Is.EqualTo("Call UIViewBase.Element instead of UQueryExtensions.Q; the project keeps that decision in one place"));
        }

        private const string Logging = @"
using System.Collections.Generic;
using UnityEngine;

class Spawner : MonoBehaviour
{
    List<int> ids = new List<int>();

    void Awake()
    {
        Debug.{|first:Log|}(""spawned"");
        Debug.{|second:Log|}(""spawned"", this);
        Debug.LogWarning(""spawned"");
        ids.{|list:Add|}(1);
    }
}";

        [TestCase("UnityEngine.Debug.Log(object) -> Log.Info", "first")]
        [TestCase("UnityEngine.Debug.Log(object, Object) -> Log.Info", "second")]
        [TestCase("UnityEngine.Debug.Log(object, UnityEngine.Object) -> Log.Info", "second")]
        [TestCase("UnityEngine.Debug.Log(System.Object) -> Log.Info", "none")]
        [TestCase("System.Collections.Generic.List.Add -> Bag.Put", "list")]
        [TestCase("System.Collections.Generic.List<T>.Add -> Bag.Put", "list")]
        [TestCase("System.Collections.Generic.List.Add(T) -> Bag.Put", "list")]
        public void ParameterList_PicksOneOverload(string entry, string reported)
        {
            string markup = Logging;
            foreach (string site in new[] { "first", "second", "list" })
            {
                markup = site == reported ? markup.Replace("{|" + site + ":", "{|TF1029:") : Markup.Without(markup, site);
            }
            AnalyzerVerifier.Verify(new DiscouragedMemberCallAnalyzer(), markup, SnippetAssembly.WithSettings("discouraged_members = " + entry));
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new DiscouragedMemberCallAnalyzer(), Views, SnippetAssembly.Tests(
                "discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element\n" +
                "discouraged_members_allowed_in = UIViewBase\n"));
        }

        [Test]
        public void BadEntry_IsReportedInTheSettingsFile()
        {
            AnalyzerVerifier.Verify(new SettingsFileAnalyzer(), "class Player { }", SnippetAssembly.WithSettings("{|TF0000:discouraged_members = UnityEngine.Debug.Log|}\n"));
        }
    }
}
