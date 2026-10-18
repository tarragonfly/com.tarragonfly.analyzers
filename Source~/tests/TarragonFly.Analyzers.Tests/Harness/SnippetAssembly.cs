using TarragonFly.Analyzers.Settings;

namespace TarragonFly.Analyzers.Tests.Harness
{
    // The Unity assembly a test snippet is compiled as.
    public sealed class SnippetAssembly
    {
        public string AssemblyName { get; set; } = "Game";
        public string FilePath { get; set; } = "Assets/Game/Code.cs";

        // Adds the test runner and NUnit references Unity gives every editor-only assembly.
        public bool EditorOnly { get; set; }

        // Contents of the settings file, with optional {|TF0000:...|} markup; null means no file.
        public string Settings { get; set; }
        public string SettingsPath { get; set; } = "Assets/Settings" + AnalyzerSettings.FileNameSuffix;
    }
}