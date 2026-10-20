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
            Assert.That(settings.CommandBaseTypes, Is.Empty);
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
                "command_base_types = ScenarioEngine.Runtime.CommandBase\n");

            Assert.That(settings.Problems, Is.Empty);
            Assert.That(settings.MaxParameters, Is.EqualTo(4));
            Assert.That(settings.MaxNesting, Is.EqualTo(2));
            Assert.That(settings.AnalyzePaths, Is.EqualTo(new[] { "Assets/Game", "Assets/Tools/" }));
            Assert.That(settings.LogPrefix, Is.EqualTo("[{Type}]"));
            Assert.That(settings.CommandBaseTypes, Is.EqualTo(new[] { "ScenarioEngine.Runtime.CommandBase" }));
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
                    "{|TF0000:max_parameters = 6|}\n"
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