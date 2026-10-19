/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: NUnit, plain compilations instead of Workspaces, test files
 *  under Assets/ so TF scoping analyzes them, no code fix verification.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public abstract class DiagnosticVerifier
{
	private const string DefaultFilePathPrefix = "Assets/Test";
	private const string CSharpDefaultFileExt = "cs";
	private const string TestProjectName = "TestProject";

	private static readonly Lazy<ImmutableArray<MetadataReference>> _references = new(
		() => [.. UnityAssemblies().Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))]);

	protected abstract DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer();

	protected virtual IEnumerable<DiagnosticAnalyzer> GetRelatedAnalyzers(DiagnosticAnalyzer analyzer)
	{
		return [];
	}

	protected static DiagnosticResult ExpectDiagnostic(DiagnosticDescriptor descriptor)
	{
		return new(descriptor);
	}

	protected DiagnosticResult ExpectDiagnostic(string diagnosticId)
	{
		var analyzer = GetCSharpDiagnosticAnalyzer();
		return ExpectDiagnostic(analyzer.SupportedDiagnostics.Single(i => i.Id == diagnosticId));
	}

	protected DiagnosticResult ExpectDiagnostic()
	{
		var analyzer = GetCSharpDiagnosticAnalyzer();
		return ExpectDiagnostic(analyzer.SupportedDiagnostics.Single());
	}

	protected Task VerifyCSharpDiagnosticAsync(string source, params DiagnosticResult[] expected)
	{
		return VerifyCSharpDiagnosticAsync(AnalyzerVerificationContext.Default, source, expected);
	}

	protected async Task VerifyCSharpDiagnosticAsync(AnalyzerVerificationContext context, string source, params DiagnosticResult[] expected)
	{
		var analyzer = GetCSharpDiagnosticAnalyzer();
		var diagnostics = await GetSortedDiagnosticsAsync(context, [source], analyzer, _references.Value);
		VerifyDiagnosticResults(diagnostics, analyzer, expected);
	}

	// Compiles without the Unity assembly named assemblyFileName, such as Unity.TextMeshPro.dll.
	protected async Task VerifyCSharpDiagnosticWithoutReferenceAsync(string assemblyFileName, string source, params DiagnosticResult[] expected)
	{
		var analyzer = GetCSharpDiagnosticAnalyzer();
		var references = _references.Value
			.Where(reference => reference.Display?.EndsWith(assemblyFileName, StringComparison.OrdinalIgnoreCase) != true)
			.ToImmutableArray();
		var diagnostics = await GetSortedDiagnosticsAsync(AnalyzerVerificationContext.Default, [source], analyzer, references);
		VerifyDiagnosticResults(diagnostics, analyzer, expected);
	}

	protected virtual void VerifyDiagnosticResults(Diagnostic[] actualResults, DiagnosticAnalyzer analyzer, params DiagnosticResult[] expectedResults)
	{
		var expectedCount = expectedResults.Length;
		var actualCount = actualResults.Length;

		if (expectedCount != actualCount)
		{
			var diagnosticsOutput = actualResults.Any() ? FormatDiagnostics(analyzer, [.. actualResults]) : "    NONE.";

			Assert.Fail($"Mismatch between number of diagnostics returned, expected \"{expectedCount}\" actual \"{actualCount}\"\r\n\r\nDiagnostics:\r\n{diagnosticsOutput}\r\n");
		}

		for (var i = 0; i < expectedResults.Length; i++)
		{
			var actual = actualResults.ElementAt(i);
			var expected = expectedResults[i];

			if (!expected.HasLocation)
			{
				if (actual.Location != Location.None)
				{
					Assert.Fail($"Expected:\nA project diagnostic with No location\nActual:\n{FormatDiagnostics(analyzer, actual)}");
				}
			}
			else
			{
				VerifyDiagnosticLocations(analyzer, actual, actual.Location, expected.Spans);
			}

			if (actual.Id != expected.Id)
			{
				Assert.Fail($"Expected diagnostic id to be \"{expected.Id}\" was \"{actual.Id}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics(analyzer, actual)}\r\n");
			}

			if (actual.Severity != expected.Severity)
			{
				Assert.Fail($"Expected diagnostic severity to be \"{expected.Severity}\" was \"{actual.Severity}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics(analyzer, actual)}\r\n");
			}

			if (actual.GetMessage() != expected.Message)
			{
				Assert.Fail($"Expected diagnostic message to be \"{expected.Message}\" was \"{actual.GetMessage()}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics(analyzer, actual)}\r\n");
			}
		}
	}

	private static void VerifyDiagnosticLocations(DiagnosticAnalyzer analyzer, Diagnostic diagnostic, Location actual, IEnumerable<DiagnosticLocation> locations)
	{
		var actualSpan = actual.GetLineSpan();
		var expected = locations.First();

		var actualLinePosition = actualSpan.StartLinePosition;

		// Only check line position if there is an actual line in the real diagnostic
		if (actualLinePosition.Line > 0)
		{
			if (actualLinePosition.Line != expected.Span.StartLinePosition.Line)
			{
				Assert.Fail($"Expected diagnostic to be on line \"{expected.Span.StartLinePosition.Line + 1}\" was actually on line \"{actualLinePosition.Line + 1}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics(analyzer, diagnostic)}\r\n");
			}
		}

		// Only check column position if there is an actual column position in the real diagnostic
		if (actualLinePosition.Character > 0)
		{
			if (actualLinePosition.Character != expected.Span.StartLinePosition.Character)
			{
				Assert.Fail($"Expected diagnostic to start at column \"{expected.Span.StartLinePosition.Character + 1}\" was actually at column \"{actualLinePosition.Character + 1}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics(analyzer, diagnostic)}\r\n");
			}
		}
	}

	private static string FormatDiagnostics(DiagnosticAnalyzer analyzer, params Diagnostic[] diagnostics)
	{
		var builder = new StringBuilder();
		for (var i = 0; i < diagnostics.Length; ++i)
		{
			var linePosition = diagnostics[i].Location.GetLineSpan().StartLinePosition;
			builder.AppendLine($"// {diagnostics[i]}");
			builder.AppendLine($"//   at line {linePosition.Line + 1}, column {linePosition.Character + 1} ({analyzer.GetType().Name})");
		}

		return builder.ToString();
	}

	private static async Task<Diagnostic[]> GetSortedDiagnosticsAsync(AnalyzerVerificationContext context, string[] sources, DiagnosticAnalyzer analyzer, ImmutableArray<MetadataReference> references)
	{
		var parseOptions = new CSharpParseOptions(context.LanguageVersion).WithPreprocessorSymbols(context.PreprocessorSymbols);
		var trees = sources
			.Select((source, index) => CSharpSyntaxTree.ParseText(source, parseOptions, DefaultFilePathPrefix + index + "." + CSharpDefaultFileExt))
			.ToArray();

		var analyzers = ImmutableArray.Create(analyzer).AddRange(new List<DiagnosticAnalyzer>());

		var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, reportSuppressedDiagnostics: true);
		var specificDiagnosticOptions = compilationOptions.SpecificDiagnosticOptions;

		// Force all tested and related diagnostics to be enabled
		foreach (var descriptor in analyzers.SelectMany(a => a.SupportedDiagnostics))
			specificDiagnosticOptions = specificDiagnosticOptions.SetItem(descriptor.Id, GetReportDiagnostic(descriptor));

		var compilation = CSharpCompilation.Create(TestProjectName, trees, references, compilationOptions.WithSpecificDiagnosticOptions(specificDiagnosticOptions));

		var optionsProvider = new AnalyzerOptionsProvider(context.Options);
		var options = new AnalyzerOptions([], optionsProvider);

		// those exceptions and handler are thrown outside the scope of the test, so make sure we do not miss them
		var analyzerExceptions = new List<Exception>();
		var analyzerOptions = new CompilationWithAnalyzersOptions(options, (e, _, _) => analyzerExceptions.Add(e), true, true, true);

		var allDiagnostics = await compilation.WithAnalyzers(analyzers, analyzerOptions).GetAllDiagnosticsAsync();
		var errors = allDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
		foreach (var error in errors)
			Assert.Fail($"Line {error.Location.GetLineSpan().StartLinePosition.Line}: {error.GetMessage()}");

		foreach (var analyzerException in analyzerExceptions)
			Assert.Fail(analyzerException.ToString());

		var diagnostics = allDiagnostics
			.Except(errors)
			.Where(d => d.Location.IsInSource && trees.Contains(d.Location.SourceTree))
			.Where(d => !context.Filters.Contains(d.Id));

		return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start)];
	}

	private static ReportDiagnostic GetReportDiagnostic(DiagnosticDescriptor descriptor)
	{
		return descriptor.DefaultSeverity switch
		{
			DiagnosticSeverity.Error => ReportDiagnostic.Error,
			DiagnosticSeverity.Warning => ReportDiagnostic.Warn,
			DiagnosticSeverity.Info or DiagnosticSeverity.Hidden => ReportDiagnostic.Info,
			_ => throw new ArgumentOutOfRangeException(nameof(descriptor))
		};
	}

	// The editor's managed assemblies, plus TextMeshPro, UI and Mathematics from its 2D project
	// template. Tests are ignored when no editor is found.
	protected static IEnumerable<string> UnityAssemblies()
	{
		var installation = UnityPath.FirstInstallation();
		if (installation == null)
			Assert.Ignore("No Unity editor found. Set UNITY_EDITOR_PATH to Unity.exe or its Editor folder.");

		var managed = Path.Combine(installation!, "Managed");
		var assemblies = new List<string> { Path.Combine(managed, "UnityEditor.dll") };

		// Package assemblies reference Unity's modules rather than the combined reference assembly.
		assemblies.AddRange(Directory.EnumerateFiles(Path.Combine(managed, "UnityEngine"), "*.dll"));

		var netstandard = Path.Combine(installation!, "NetStandard");
		assemblies.Add(Path.Combine(netstandard, "ref", "2.1.0", "netstandard.dll"));
		assemblies.AddRange(Directory.EnumerateFiles(Path.Combine(netstandard, "compat", "2.1.0", "shims", "netfx"), "*.dll"));

		var libcache = Path.Combine(installation!, "Resources", "PackageManager", "ProjectTemplates", "libcache");
		var template2d = Directory.GetDirectories(libcache, "com.unity.template.2d-*").Single();
		var template2dScriptAssemblies = Path.Combine(template2d, "ScriptAssemblies");
		assemblies.Add(Path.Combine(template2dScriptAssemblies, "Unity.Mathematics.dll"));
		assemblies.Add(Path.Combine(template2dScriptAssemblies, "UnityEngine.UI.dll"));
		assemblies.Add(Path.Combine(template2dScriptAssemblies, "Unity.TextMeshPro.dll"));
		return assemblies;
	}
}