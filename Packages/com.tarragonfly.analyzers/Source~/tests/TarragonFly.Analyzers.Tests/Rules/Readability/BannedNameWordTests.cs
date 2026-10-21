using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1026 name uses a word the project avoids. The words here are made up so the test source
    // stays clean of any real project's list.
    public class BannedNameWordTests
    {
        private const string Settings = "banned_name_words = widget, flux capacitor\nbanned_method_prefixes = frob\n";

        private const string Declarations = @"
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.{|TF1026:Widget|}.Core
{
    class {|TF1026:WidgetSpawner|}<{|TF1026:TWidget|}>
    {
        int {|TF1026:widgetCount|};
        int {|TF1026:widget_total|};
        int {|TF1026:_fluxCapacitor|};
        string {|TF1026:WidgetName|} { get; set; }
        event Action {|TF1026:WidgetSpawned|};
        event Action {|TF1026:OnWidgetLost|}, {|TF1026:OnWidgetFound|};

        void {|TF1026:SpawnWidget|}(int {|TF1026:widgetId|})
        {
            int {|TF1026:widgetIndex|} = widgetId;
            foreach (int {|TF1026:widget|} in new[] { 1 }) { }
            if (int.TryParse(""1"", out int {|TF1026:parsedWidget|})) { }
            object boxed = this;
            if (boxed is WidgetSpawner<int> {|TF1026:widgetSpawner|}) { }
            try { } catch (Exception {|TF1026:widgetError|}) { }
            Func<int, int> twice = {|TF1026:widgetArg|} => widgetArg * 2;
            int {|TF1026:FluxCapacitorCharge|}() => 1;
            IEnumerable<int> query = from {|TF1026:widgetItem|} in new[] { 1 } select widgetItem;
        }
    }

    enum Part { {|TF1026:Widget|}, Gear }
    delegate void {|TF1026:WidgetHandler|}();
    struct {|TF1026:WidgetId|} { }
    interface {|TF1026:IWidget|} { }
}";

        private const string UsesAndInheritedNames = @"
using System;

namespace Game.Widgets
{
    class Midwidget { }
    class Widgets { }
    class FluxDriveCapacitor { }
    class CapacitorFlux { }
    class Frobnicate { }

    [AttributeUsage(AttributeTargets.All)]
    class {|TF1026:WidgetAttribute|} : Attribute { }

    [Widget]
    class Spawner
    {
        void Run(Midwidget target)
        {
            Midwidget local = new Midwidget();
            local.ToString();
            Type type = typeof(Widgets);
        }
    }

    abstract class Base
    {
        public abstract void {|TF1026:SpawnWidget|}();
        public abstract int {|TF1026:WidgetCount|} { get; }
    }

    interface {|TF1026:IWidgetSource|}
    {
        void {|TF1026:LoadWidget|}();
    }

    class Derived : Base, IWidgetSource
    {
        public override void SpawnWidget() { }
        public override int WidgetCount => 1;
        void IWidgetSource.LoadWidget() { }
    }

    class {|TF1026:WidgetPool|}
    {
        public WidgetPool() { }
        ~WidgetPool() { }
    }
}";

        // SAVED marks the names Unity writes into scenes, prefabs and assets.
        private const string SavedFields = @"
using System;
using UnityEngine;

class {|TF1026:WidgetBehaviour|} : MonoBehaviour
{
    [SerializeField] int {|SAVED:widgetCount|};
    [SerializeReference] object {|SAVED:widgetReference|};
    public int {|SAVED:widgetSize|};
    [field: SerializeField] int {|SAVED:WidgetHealth|} { get; set; }
    public static int {|TF1026:widgetTotal|};
    public readonly int {|TF1026:widgetMax|};
    [NonSerialized] public int {|TF1026:widgetScratch|};
    int {|TF1026:widgetPrivate|};
    int {|TF1026:WidgetPlain|} { get; set; }
}

[Serializable]
class {|TF1026:WidgetData|}
{
    public int {|SAVED:widgetId|};
    int {|TF1026:widgetHidden|};
}

class {|TF1026:WidgetSettings|}
{
    public int {|TF1026:widgetId|};
}";

        [Test]
        public void DeclaredNames_AreReported()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), Declarations, SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void UsesOfANameAndNamesTakenFromABase_AreNotReported()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), UsesAndInheritedNames, SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void FieldsUnitySaves_AreSkippedByDefault()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), Markup.Without(SavedFields, "SAVED"), SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void FieldsUnitySaves_AreReportedWhenTheSkipIsOff()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), SavedFields.Replace("{|SAVED:", "{|TF1026:"), SnippetAssembly.WithSettings(Settings + "banned_name_words_skip_serialized = false\n"));
        }

        [Test]
        public void MethodPrefix_IsReportedOnMethodsAndLocalFunctionsOnly()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), @"
class Loader
{
    int FrobCount { get; set; }
    int frobTotal;

    void {|TF1026:FrobCache|}() { }
    void {|TF1026:Frob|}() { }
    void Frobnicate() { }
    void CacheFrob() { }

    void Run()
    {
        int frobLocal = 0;
        void {|TF1026:FrobLocal|}() { }
    }
}", SnippetAssembly.WithSettings(Settings));
        }

        [Test]
        public void FileName_IsReportedAtTheStartOfTheFile()
        {
            SnippetAssembly file = new SnippetAssembly { FilePath = "Assets/Game/WidgetPool.cs", Settings = Settings };
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), "{|TF1026:|}class Pool { }", file);
            Assert.That(AnalyzerVerifier.Messages(new BannedNameWordAnalyzer(), "class Pool { }", file),
                Is.EqualTo(new[] { "'WidgetPool.cs' uses 'widget', a word this project avoids; name what it actually does" }));
        }

        [Test]
        public void Message_NamesTheEntryAsWritten()
        {
            Assert.That(AnalyzerVerifier.Messages(new BannedNameWordAnalyzer(), "class Pool { int fluxCapacitorCharge; void FrobAll() { } }", SnippetAssembly.WithSettings(Settings)),
                Is.EqualTo(new[]
                {
                    "'fluxCapacitorCharge' uses 'flux capacitor', a word this project avoids; name what it actually does",
                    "'FrobAll' uses 'frob', a word this project avoids; name what it actually does"
                }));
        }

        [Test]
        public void WithoutWordsInTheSettings_NothingIsReported()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), Markup.Without(Declarations, "TF1026"));
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), Markup.Without(Declarations, "TF1026"), SnippetAssembly.WithSettings("banned_name_words = \n"));
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new BannedNameWordAnalyzer(), Declarations, SnippetAssembly.Tests(Settings));
        }
    }
}
