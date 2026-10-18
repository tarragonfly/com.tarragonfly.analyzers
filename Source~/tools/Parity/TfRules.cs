using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Parity
{
    // Every analyzer and rule in TarragonFly.Analyzers.dll, found by reflection as Unity would find them.
    public static class TfRules
    {
        public static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
        {
            Assembly assembly = typeof(Descriptors).Assembly;
            return assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t))
                .ToImmutableArray();
        }

        public static IReadOnlyList<DiagnosticDescriptor> LoadDescriptors()
        {
            return LoadAnalyzers()
                .SelectMany(a => a.SupportedDiagnostics)
                .GroupBy(d => d.Id)
                .Select(g => g.First())
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();
        }
    }
}