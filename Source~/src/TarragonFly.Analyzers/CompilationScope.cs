using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Settings;

namespace TarragonFly.Analyzers
{
    // What TF rules check in one compilation (one Unity assembly): its kind of code, its settings,
    // and which of its files are in the analyzed folders. Shared by every TF analyzer in that compilation.
    public sealed class CompilationScope
    {
        private const string TestRunnerAssembly = "UnityEngine.TestRunner";

        private static readonly ConditionalWeakTable<Compilation, CompilationScope> Scopes = new ConditionalWeakTable<Compilation, CompilationScope>();

        private readonly ConcurrentDictionary<SyntaxTree, bool> analyzedTrees = new ConcurrentDictionary<SyntaxTree, bool>();

        private CompilationScope(AnalyzerSettings settings, CodeKind assemblyKind)
        {
            Settings = settings;
            AssemblyKind = assemblyKind;
        }

        public AnalyzerSettings Settings { get; }
        public CodeKind AssemblyKind { get; }

        public static CompilationScope Get(Compilation compilation, AnalyzerOptions options, CancellationToken ct)
        {
            return Scopes.GetValue(compilation, c =>
            {
                AnalyzerSettings settings = AnalyzerSettings.Read(options, ct);
                return new CompilationScope(settings, GetAssemblyKind(c, settings));
            });
        }

        // Test names win over editor names, so "Game.Editor.Tests" is test code.
        public static CodeKind GetAssemblyKind(Compilation compilation, AnalyzerSettings settings)
        {
            string name = compilation.AssemblyName;
            if (Glob.MatchesAny(settings.TestAssemblies, name)) return CodeKind.Tests;
            if (Glob.MatchesAny(settings.EditorAssemblies, name)) return CodeKind.Editor;

            // With the Test Framework package installed, Unity references the test runner from every
            // editor-only assembly and from no runtime one.
            foreach (AssemblyIdentity reference in compilation.ReferencedAssemblyNames)
            {
                if (reference.Name == TestRunnerAssembly) return CodeKind.Editor;
            }
            return CodeKind.Runtime;
        }

        public bool Checks(DiagnosticDescriptor rule)
        {
            return (Descriptors.GetChecks(rule) & AssemblyKind) != 0;
        }

        public bool ChecksAny(ImmutableArray<DiagnosticDescriptor> rules)
        {
            foreach (DiagnosticDescriptor rule in rules)
            {
                if (Checks(rule)) return true;
            }
            return false;
        }

        public bool IsAnalyzed(SyntaxTree tree)
        {
            return analyzedTrees.GetOrAdd(tree, t => IsAnalyzedPath(t.FilePath));
        }

        public bool IsAnalyzedPath(string filePath)
        {
            string path = ProjectPaths.ToProjectRelative(filePath);
            foreach (string folder in Settings.AnalyzePaths)
            {
                if (ProjectPaths.IsUnder(path, folder)) return true;
            }
            return false;
        }

        public bool HasAnalyzedFiles(Compilation compilation)
        {
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                if (IsAnalyzed(tree)) return true;
            }
            return false;
        }
    }
}