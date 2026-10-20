using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using TarragonFly.Analyzers.Testing;

namespace TarragonFly.Analyzers.Tests.Harness
{
    // Compiles a snippet against the stubs, runs one analyzer and compares its diagnostics with the
    // {|ID:...|} markup. A snippet that does not compile, or an analyzer that throws, fails the test.
    public static class AnalyzerVerifier
    {
        public static void Verify(DiagnosticAnalyzer analyzer, string sourceMarkup, SnippetAssembly assembly = null)
        {
            assembly ??= new SnippetAssembly();
            List<(string Id, TextSpan Span)> expectedInSource = new List<(string Id, TextSpan Span)>();
            List<(string Id, TextSpan Span)> expectedInSettings = new List<(string Id, TextSpan Span)>();
            string source = Markup.Parse(sourceMarkup, expectedInSource);
            string settings = assembly.Settings == null ? null : Markup.Parse(assembly.Settings, expectedInSettings);

            ImmutableArray<Diagnostic> actual = Run(analyzer, source, assembly, settings);

            List<string> expected = expectedInSource.Select(e => Describe(e.Id, assembly.FilePath, e.Span))
                .Concat(expectedInSettings.Select(e => Describe(e.Id, assembly.SettingsPath, e.Span)))
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
            List<string> reported = actual.Select(d => Describe(d.Id, d.Location.GetLineSpan().Path, d.Location.SourceSpan))
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
            Assert.That(reported, Is.EqualTo(expected), "Reported diagnostics:\n" + string.Join("\n", actual));
        }

        public static ImmutableArray<Diagnostic> Run(DiagnosticAnalyzer analyzer, string source, SnippetAssembly assembly = null, string settings = null)
        {
            assembly ??= new SnippetAssembly();
            settings ??= assembly.Settings;
            CSharpCompilation compilation = Compile(source, assembly);

            ImmutableArray<AdditionalText> additionalFiles = settings == null
                ? ImmutableArray<AdditionalText>.Empty
                : ImmutableArray.Create<AdditionalText>(new TestAdditionalText(assembly.SettingsPath, settings));
            List<string> analyzerFailures = new List<string>();
            CompilationWithAnalyzersOptions options = new CompilationWithAnalyzersOptions(
                new AnalyzerOptions(additionalFiles),
                onAnalyzerException: (exception, failed, _) => analyzerFailures.Add($"{failed.GetType().Name}: {exception}"),
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false);

            ImmutableArray<Diagnostic> diagnostics = compilation
                .WithAnalyzers(ImmutableArray.Create(analyzer), options)
                .GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(analyzerFailures, Is.Empty, "The analyzer threw.");
            return diagnostics;
        }

        public static CSharpCompilation Compile(string source, SnippetAssembly assembly = null)
        {
            assembly ??= new SnippetAssembly();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, assembly.ParseOptions ?? CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9), assembly.FilePath);
            ImmutableArray<MetadataReference> references = assembly.EditorOnly ? StubAssemblies.EditorReferences : StubAssemblies.GameReferences;
            references = references.AddRange(assembly.ReferencedAssemblies.Select(other => BuildReference(other.Key, other.Value, references)).ToArray());
            CSharpCompilation compilation = CSharpCompilation.Create(
                assembly.AssemblyName, new[] { tree }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable, allowUnsafe: assembly.AllowUnsafe));

            Diagnostic[] errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.That(errors, Is.Empty, "The test snippet does not compile.");
            return compilation;
        }

        // Builds another assembly and references it from that path, as Unity references the others it builds.
        private static MetadataReference BuildReference(string path, string source, ImmutableArray<MetadataReference> references)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            CSharpCompilation other = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path), new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (MemoryStream image = new MemoryStream())
            {
                EmitResult result = other.Emit(image);
                Assert.That(result.Success, Is.True, "A referenced assembly does not compile:\n" + string.Join("\n", result.Diagnostics));
                return MetadataReference.CreateFromImage(image.ToArray(), filePath: path);
            }
        }

        private static string Describe(string id, string path, TextSpan span)
        {
            return $"{id} {path} {span}";
        }
    }
}