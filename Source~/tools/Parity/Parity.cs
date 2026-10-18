using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Testing;

namespace TarragonFly.Analyzers.Parity
{
    // The parity report: for each TF rule that replaces a Microsoft rule, where the two
    // agree and differ. Inputs are the corpus (one file per source rule, named UNT0008.cs or UNT0008.case.cs,
    // compiled once as game code and once as tests) and optionally Analyze.ps1's findings.json for a project.
    public static class ParityRun
    {
        private sealed class Input
        {
            public string Name;
            public List<(string Rule, string Where)> Results = new List<(string Rule, string Where)>();
        }

        public static int Run(string reportPath, string corpusFolder, List<string> oraclePaths, string findingsPath)
        {
            List<string> errors = new List<string>();
            IReadOnlyList<DiagnosticDescriptor> tfRules = TfRules.LoadDescriptors();
            List<(string Tf, string Source)> pairs = tfRules
                .Where(r => Descriptors.GetSourceRule(r) != null)
                .Select(r => (r.Id, Descriptors.GetSourceRule(r)))
                .ToList();

            ImmutableArray<DiagnosticAnalyzer> oracles = LoadOracles(oraclePaths, errors);
            ImmutableArray<DiagnosticAnalyzer> analyzers = TfRules.LoadAnalyzers().AddRange(oracles);
            HashSet<string> oracleIds = new HashSet<string>(oracles.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id), StringComparer.Ordinal);

            string[] corpusFiles = Directory.Exists(corpusFolder)
                ? Directory.GetFiles(corpusFolder, "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : new string[0];
            List<Input> inputs = new List<Input>();
            foreach (string file in corpusFiles)
            {
                string sourceRule = Path.GetFileName(file).Split('.')[0];
                inputs.Add(RunCorpusFile(file, sourceRule, "game", analyzers, oracleIds, errors));
                inputs.Add(RunCorpusFile(file, sourceRule, "tests", analyzers, oracleIds, errors));
            }
            if (findingsPath != null) inputs.Add(ReadFindings(findingsPath));

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, Report(pairs, corpusFiles, inputs, oracles.Length, findingsPath != null, errors), new UTF8Encoding(false));
            Console.WriteLine($"Wrote {reportPath}: {pairs.Count} mapped rule(s), {corpusFiles.Length} corpus file(s), {errors.Count} problem(s).");
            foreach (string error in errors) Console.Error.WriteLine(error);
            return errors.Count == 0 ? 0 : 1;
        }

        private static ImmutableArray<DiagnosticAnalyzer> LoadOracles(List<string> paths, List<string> errors)
        {
            ImmutableArray<DiagnosticAnalyzer>.Builder loaded = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
            foreach (string path in paths)
            {
                AnalyzerFileReference reference = new AnalyzerFileReference(path, new OracleLoader());
                reference.AnalyzerLoadFailed += (_, e) => errors.Add($"{Path.GetFileName(path)}: {e.Message}");
                ImmutableArray<DiagnosticAnalyzer> found = reference.GetAnalyzers(LanguageNames.CSharp);
                if (found.IsEmpty) errors.Add($"{Path.GetFileName(path)}: no C# analyzers found.");
                loaded.AddRange(found);
            }
            return loaded.ToImmutable();
        }

        // Only the corpus file's own source rule (and the TF rules) report; every other oracle rule is off.
        private static Input RunCorpusFile(string file, string sourceRule, string mode, ImmutableArray<DiagnosticAnalyzer> analyzers, HashSet<string> oracleIds, List<string> errors)
        {
            string name = $"corpus {Path.GetFileName(file)} ({mode})";
            bool tests = mode == "tests";
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9), "Assets/Parity/" + Path.GetFileName(file));
            Dictionary<string, ReportDiagnostic> ruleOptions = oracleIds.ToDictionary(id => id, id => id == sourceRule ? ReportDiagnostic.Warn : ReportDiagnostic.Suppress);
            CSharpCompilation compilation = CSharpCompilation.Create(
                tests ? "Parity.Tests" : "Parity.Game",
                new[] { tree },
                tests ? StubAssemblies.EditorReferences : StubAssemblies.GameReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, specificDiagnosticOptions: ruleOptions));

            foreach (Diagnostic error in compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                errors.Add($"{name} does not compile: {error}");
            }

            CompilationWithAnalyzersOptions options = new CompilationWithAnalyzersOptions(
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                onAnalyzerException: (exception, analyzer, _) => errors.Add($"{name}: {analyzer.GetType().Name} threw {exception.GetType().Name}: {exception.Message}"),
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false);
            ImmutableArray<Diagnostic> diagnostics = compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();

            Input input = new Input { Name = name };
            foreach (Diagnostic diagnostic in diagnostics.Where(d => d.Severity != DiagnosticSeverity.Hidden))
            {
                FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                input.Results.Add((diagnostic.Id, $"line {span.StartLinePosition.Line + 1}, column {span.StartLinePosition.Character + 1}"));
            }
            return input;
        }

        private static Input ReadFindings(string path)
        {
            Input input = new Input { Name = "project (" + Path.GetFileName(path) + ")" };
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement finding in document.RootElement.EnumerateArray())
            {
                string file = finding.GetProperty("File").GetString();
                string where = string.IsNullOrEmpty(file)
                    ? "(no location)"
                    : $"{file}:{finding.GetProperty("Line").GetInt32()}:{finding.GetProperty("Column").GetInt32()}";
                input.Results.Add((finding.GetProperty("Rule").GetString(), where));
            }
            return input;
        }

        private static string Report(List<(string Tf, string Source)> pairs, string[] corpusFiles, List<Input> inputs, int oracleCount, bool hasProject, List<string> errors)
        {
            StringBuilder text = new StringBuilder();
            text.Append("# Parity report\r\n\r\n");
            text.Append("Written by `Tools~/Check.ps1`. Every difference is either a bug to fix or an intended difference noted in the rule's source file.\r\n\r\n");
            text.Append($"- Rules compared: {pairs.Count}\r\n");
            text.Append($"- Corpus files: {corpusFiles.Length}, each compiled as game code and as tests\r\n");
            text.Append($"- Oracle analyzers loaded: {oracleCount}\r\n");
            text.Append(hasProject ? "- Project findings: included\r\n" : "- Project findings: not included (run Check.ps1 -Project)\r\n");

            if (errors.Count > 0)
            {
                text.Append("\r\n## Problems\r\n\r\n");
                foreach (string error in errors) text.Append($"- {error}\r\n");
            }

            foreach ((string tf, string source) in pairs)
            {
                text.Append($"\r\n## {tf} replaces {source}\r\n\r\n");
                text.Append("| Input | Matched | TF only | Source only |\r\n|---|---|---|---|\r\n");
                List<string> differences = new List<string>();
                foreach (Input input in inputs)
                {
                    HashSet<string> tfAt = new HashSet<string>(input.Results.Where(r => r.Rule == tf).Select(r => r.Where), StringComparer.Ordinal);
                    HashSet<string> sourceAt = new HashSet<string>(input.Results.Where(r => r.Rule == source).Select(r => r.Where), StringComparer.Ordinal);
                    if (tfAt.Count == 0 && sourceAt.Count == 0) continue;
                    List<string> tfOnly = tfAt.Except(sourceAt).OrderBy(w => w, StringComparer.Ordinal).ToList();
                    List<string> sourceOnly = sourceAt.Except(tfAt).OrderBy(w => w, StringComparer.Ordinal).ToList();
                    text.Append($"| {input.Name} | {tfAt.Intersect(sourceAt).Count()} | {tfOnly.Count} | {sourceOnly.Count} |\r\n");
                    differences.AddRange(tfOnly.Select(w => $"- {input.Name}, {w}: only {tf}"));
                    differences.AddRange(sourceOnly.Select(w => $"- {input.Name}, {w}: only {source}"));
                }
                if (differences.Count > 0) text.Append("\r\n" + string.Join("\r\n", differences) + "\r\n");
            }

            HashSet<string> mappedSources = new HashSet<string>(pairs.Select(p => p.Source), StringComparer.Ordinal);
            List<string> notPorted = corpusFiles.Select(f => Path.GetFileName(f).Split('.')[0]).Distinct().Where(s => !mappedSources.Contains(s)).ToList();
            if (notPorted.Count > 0)
            {
                text.Append("\r\n## Corpus files with no TF rule yet\r\n\r\n");
                foreach (string source in notPorted)
                {
                    IEnumerable<string> counts = inputs.Where(i => i.Name.StartsWith("corpus " + source + ".", StringComparison.Ordinal))
                        .Select(i => $"{i.Name}: {i.Results.Count(r => r.Rule == source)}");
                    text.Append($"- {source}: {string.Join("; ", counts)}\r\n");
                }
            }
            return text.ToString();
        }

        // Loads each oracle DLL into the default context, where it binds to this tool's Roslyn.
        private sealed class OracleLoader : IAnalyzerAssemblyLoader
        {
            public void AddDependencyLocation(string fullPath)
            {
            }

            public Assembly LoadFromPath(string fullPath)
            {
                return Assembly.LoadFrom(fullPath);
            }
        }
    }
}