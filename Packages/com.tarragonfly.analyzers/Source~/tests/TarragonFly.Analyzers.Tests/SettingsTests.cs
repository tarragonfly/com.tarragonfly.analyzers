using System.Linq;
using System.Threading;
using NUnit.Framework;
using TarragonFly.Analyzers.Settings;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests
{
    public class SettingsTests
    {
        private const string Code = "class Player { }";

        [Test]
        public void MissingFile_UsesDefaults()
        {
            AnalyzerSettings settings = AnalyzerSettings.Read(new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(default), CancellationToken.None);

            Assert.That(settings.File, Is.Null);
            Assert.That(settings.MaxParameters, Is.EqualTo(7));
            Assert.That(settings.MaxCognitiveComplexity, Is.EqualTo(15));
            Assert.That(settings.MaxConditionOperators, Is.EqualTo(4));
            Assert.That(settings.MaxSwitchSectionStatements, Is.EqualTo(8));
            Assert.That(settings.AnalyzePaths, Is.EqualTo(new[] { "Assets/" }));
            Assert.That(settings.AllowedServiceSetTypes, Is.Empty);
            Assert.That(settings.ReportVarIn, Is.EqualTo(CodeKind.None));
            Assert.That(settings.BannedNameWords, Is.Empty);
            Assert.That(settings.BannedMethodPrefixes, Is.Empty);
            Assert.That(settings.BannedNameWordsSkipSerialized, Is.True);
            Assert.That(settings.AsciiOnly, Is.False);
            Assert.That(settings.NonAsciiAllowedIn, Is.Empty);
            Assert.That(settings.DiscouragedMembers, Is.Empty);
            Assert.That(settings.DiscouragedMembersAllowedIn, Is.Empty);
            Assert.That(settings.TestSupportAssemblies, Is.Empty);
        }

        [Test]
        public void ValidLines_AreApplied_AndCommentsSkipped()
        {
            AnalyzerSettings settings = Parse(
                "# thresholds\n" +
                "max_parameters = 4\n" +
                "\n" +
                "  max_nesting=2  \n" +
                "analyze_paths = Assets/Game, Assets/Tools/ ,\n" +
                "log_prefix = [{Type}] \n" +
                "allowed_service_set_types = Game.UI.ViewManager\n");

            Assert.That(settings.Problems, Is.Empty);
            Assert.That(settings.MaxParameters, Is.EqualTo(4));
            Assert.That(settings.MaxNesting, Is.EqualTo(2));
            Assert.That(settings.AnalyzePaths, Is.EqualTo(new[] { "Assets/Game", "Assets/Tools/" }));
            Assert.That(settings.LogPrefix, Is.EqualTo("[{Type}]"));
            Assert.That(settings.AllowedServiceSetTypes, Is.EqualTo(new[] { "Game.UI.ViewManager" }));
        }

        [Test]
        public void RuleSwitches_AreApplied_WhateverTheirCase()
        {
            AnalyzerSettings settings = Parse(
                "report_var = Game\n" +
                "banned_name_words = widget, flux capacitor\n" +
                "banned_method_prefixes = frob\n" +
                "banned_name_words_skip_serialized = False\n" +
                "ascii_only = TRUE\n" +
                "non_ascii_allowed_in = Assets/Game/Dialogue/*, Assets/Banners/*\n");

            Assert.That(settings.Problems, Is.Empty);
            Assert.That(settings.ReportVarIn, Is.EqualTo(CodeKind.Game));
            Assert.That(settings.BannedNameWords, Is.EqualTo(new[] { "widget", "flux capacitor" }));
            Assert.That(settings.BannedMethodPrefixes, Is.EqualTo(new[] { "frob" }));
            Assert.That(settings.BannedNameWordsSkipSerialized, Is.False);
            Assert.That(settings.AsciiOnly, Is.True);
            Assert.That(settings.NonAsciiAllowedIn, Is.EqualTo(new[] { "Assets/Game/Dialogue/*", "Assets/Banners/*" }));
        }

        // Commas inside a parameter list belong to the entry, not the list.
        [Test]
        public void DiscouragedMembers_AreParsed_WithOptionalParameterLists()
        {
            AnalyzerSettings settings = Parse(
                "discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element, UnityEngine.Debug.Log(object , UnityEngine.Object) -> Log.Info, System.Collections.Generic.List<T>.Add -> Bag.Put\n" +
                "discouraged_members_allowed_in = UIViewBase, Game.Logging.Log\n" +
                "test_support_assemblies = *.Tests.Shared\n");

            Assert.That(settings.Problems, Is.Empty);
            Assert.That(settings.DiscouragedMembers.Select(m => (m.TypeName, m.MemberName, m.ParameterTypes, m.Replacement)), Is.EqualTo(new[]
            {
                ("UnityEngine.UIElements.UQueryExtensions", "Q", (string)null, "UIViewBase.Element"),
                ("UnityEngine.Debug", "Log", "object, UnityEngine.Object", "Log.Info"),
                ("System.Collections.Generic.List", "Add", (string)null, "Bag.Put")
            }));
            Assert.That(settings.DiscouragedMembersAllowedIn, Is.EqualTo(new[] { "UIViewBase", "Game.Logging.Log" }));
            Assert.That(settings.TestSupportAssemblies, Is.EqualTo(new[] { "*.Tests.Shared" }));
        }

        [TestCase("discouraged_members = UnityEngine.Debug.Log")]
        [TestCase("discouraged_members = UnityEngine.Debug.Log ->")]
        [TestCase("discouraged_members = Log -> Log.Info")]
        [TestCase("discouraged_members = UnityEngine.Debug.Log(object -> Log.Info")]
        [TestCase("discouraged_members = UnityEngine.Debug.Log -> Log.Info, Broken")]
        public void BadDiscouragedMember_KeepsTheListEmpty_AndReportsTheLine(string line)
        {
            AnalyzerSettings settings = Parse(line);

            Assert.That(settings.DiscouragedMembers, Is.Empty);
            Assert.That(settings.Problems, Has.Length.EqualTo(1));
        }

        [TestCase("report_var = sometimes")]
        [TestCase("ascii_only = yes")]
        [TestCase("banned_name_words_skip_serialized = 1")]
        public void BadWord_KeepsDefault_AndReportsTheLine(string line)
        {
            AnalyzerSettings settings = Parse(line);

            Assert.That(settings.ReportVarIn, Is.EqualTo(CodeKind.None));
            Assert.That(settings.AsciiOnly, Is.False);
            Assert.That(settings.BannedNameWordsSkipSerialized, Is.True);
            Assert.That(settings.Problems, Has.Length.EqualTo(1));
        }

        [TestCase("max_parameters = seven")]
        [TestCase("max_parameters = 0")]
        [TestCase("max_parameters = -3")]
        public void BadNumber_KeepsDefault_AndReportsTheLine(string line)
        {
            AnalyzerSettings settings = Parse(line);

            Assert.That(settings.MaxParameters, Is.EqualTo(7));
            Assert.That(settings.Problems, Has.Length.EqualTo(1));
        }

        [Test]
        public void ProblemLines_AreReportedAsTF0000_InTheSettingsFile()
        {
            AnalyzerVerifier.Verify(new SettingsFileAnalyzer(), Code, new SnippetAssembly
            {
                Settings =
                    "max_parameters = 5\n" +
                    "  {|TF0000:max_parmeters = 5|}\n" +
                    "{|TF0000:max_nesting|}\n" +
                    "{|TF0000:max_parameters = 6|}\n" +
                    "{|TF0000:report_var = sometimes|}\n" +
                    "{|TF0000:ascii_only = yes|}\n"
            });
        }

        [Test]
        public void NoSettingsFile_ReportsNothing()
        {
            AnalyzerVerifier.Verify(new SettingsFileAnalyzer(), Code);
        }

        // Every Unity assembly gets the settings file; only those with analyzed files repeat the warning.
        [Test]
        public void AssemblyWithoutAnalyzedFiles_DoesNotReportTheFile()
        {
            AnalyzerVerifier.Verify(new SettingsFileAnalyzer(), Code, new SnippetAssembly
            {
                FilePath = "Packages/com.example.tool/Runtime/Tool.cs",
                Settings = "unknown_key = 1\n"
            });
        }

        private static AnalyzerSettings Parse(string text)
        {
            return AnalyzerSettings.Read(new TestAdditionalText("Assets/Settings" + AnalyzerSettings.FileNameSuffix, text), CancellationToken.None);
        }
    }
}