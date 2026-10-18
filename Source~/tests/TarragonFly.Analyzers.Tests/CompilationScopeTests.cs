using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using TarragonFly.Analyzers.Settings;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests
{
    public class CompilationScopeTests
    {
        [TestCase("Game", false, CodeKind.Runtime)]
        [TestCase("MyGame.UI", false, CodeKind.Runtime)]
        [TestCase("ScenarioEngine.Editor", true, CodeKind.Editor)]
        [TestCase("Assembly-CSharp-Editor", true, CodeKind.Editor)]
        [TestCase("Assembly-CSharp-Editor-firstpass", true, CodeKind.Editor)]
        [TestCase("ScenarioEngine.Editor.Tests", true, CodeKind.Tests)]
        [TestCase("MyGame.UI.Tests", true, CodeKind.Tests)]
        [TestCase("LevelTools", true, CodeKind.Editor)]
        public void AssemblyKind_FollowsNameAndUnityReferences(string assemblyName, bool editorOnly, CodeKind expected)
        {
            Compilation compilation = AnalyzerVerifier.Compile("class A { }", new SnippetAssembly { AssemblyName = assemblyName, EditorOnly = editorOnly });

            Assert.That(CompilationScope.GetAssemblyKind(compilation, AnalyzerSettings.Defaults), Is.EqualTo(expected));
        }

        [Test]
        public void AssemblyKind_UsesTheSettingsPatterns()
        {
            Compilation compilation = AnalyzerVerifier.Compile("class A { }", new SnippetAssembly { AssemblyName = "Game.Specs", EditorOnly = true });
            AnalyzerSettings settings = Settings("test_assemblies = *.Specs");

            Assert.That(CompilationScope.GetAssemblyKind(compilation, settings), Is.EqualTo(CodeKind.Tests));
        }

        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Packages/com.example.tool/Runtime/Tool.cs", false)]
        [TestCase("Library/PackageCache/com.unity.ugui/Runtime/Button.cs", false)]
        [TestCase(@"C:\Projects\Game\Assets\Game\Player.cs", true)]
        [TestCase("/home/dev/Game/Library/PackageCache/x/Y.cs", false)]
        [TestCase("Assets.cs", false)]
        public void DefaultPaths_AnalyzeOnlyAssets(string path, bool expected)
        {
            Assert.That(Scope("").IsAnalyzedPath(path), Is.EqualTo(expected));
        }

        [TestCase("Assets/Game/Player.cs", true)]
        [TestCase("Assets/ThirdParty/Plugin.cs", false)]
        [TestCase("Packages/com.me.tools/Editor/Tool.cs", true)]
        [TestCase("Assets/Loose.cs", true)]
        public void ConfiguredPaths_TakeFoldersAndFiles(string path, bool expected)
        {
            CompilationScope scope = Scope("analyze_paths = Assets/Game, Packages/com.me.tools, Assets/Loose.cs");

            Assert.That(scope.IsAnalyzedPath(path), Is.EqualTo(expected));
        }

        [Test]
        public void RulesOnlyRunOnTheCodeTheyCheck()
        {
            SnippetAssembly runtime = new SnippetAssembly();
            SnippetAssembly tests = new SnippetAssembly { AssemblyName = "Game.Tests", EditorOnly = true };
            SnippetAssembly package = new SnippetAssembly { FilePath = "Packages/com.example.tool/Runtime/Tool.cs" };

            AnalyzerVerifier.Verify(new ProbeAnalyzer(), "class {|TF9000:Player|} { }", runtime);
            AnalyzerVerifier.Verify(new ProbeAnalyzer(), "class Player { }", tests);
            AnalyzerVerifier.Verify(new ProbeAnalyzer(), "class Player { }", package);
        }

        private static CompilationScope Scope(string settings)
        {
            Compilation compilation = AnalyzerVerifier.Compile("class A { }");
            AnalyzerOptions options = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(SettingsFile(settings)));
            return CompilationScope.Get(compilation, options, CancellationToken.None);
        }

        private static AnalyzerSettings Settings(string text)
        {
            return AnalyzerSettings.Read(SettingsFile(text), CancellationToken.None);
        }

        private static TestAdditionalText SettingsFile(string text)
        {
            return new TestAdditionalText("Assets/Settings" + AnalyzerSettings.FileNameSuffix, text);
        }

        // Reports every class in runtime code, through the same base class TF rules use.
        [DiagnosticAnalyzer(LanguageNames.CSharp)]
        private sealed class ProbeAnalyzer : ScopedAnalyzer
        {
            private static readonly DiagnosticDescriptor Rule = Descriptors.Create("TF9000", "Probe", "Probe", "Tests", CodeKind.Runtime);

            public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

            protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
            {
                start.RegisterSymbolAction(context =>
                {
                    foreach (Location location in context.Symbol.Locations)
                    {
                        if (location.IsInSource && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location));
                    }
                }, SymbolKind.NamedType);
            }
        }
    }
}