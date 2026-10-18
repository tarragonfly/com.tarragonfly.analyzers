using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers
{
    // Builds every TF rule's descriptor so they all carry the same tags.
    public static class Descriptors
    {
        private const string ChecksTagPrefix = "checks:";
        private const string SourceRuleTagPrefix = "source:";

        // sourceRule is the Microsoft.Unity.Analyzers or NetAnalyzers rule this one replaces, for parity runs.
        // description is the longer explanation Rider and the Unity Console show with the warning.
        public static DiagnosticDescriptor Create(
            string id,
            string title,
            string messageFormat,
            string area,
            CodeKind checks,
            DiagnosticSeverity severity = DiagnosticSeverity.Warning,
            string sourceRule = null,
            string description = null,
            params string[] extraTags)
        {
            if (checks == CodeKind.None) throw new ArgumentException("A rule must check at least one kind of code.", nameof(checks));

            List<string> tags = new List<string>();
            if ((checks & CodeKind.Runtime) != 0) tags.Add(ChecksTagPrefix + "runtime");
            if ((checks & CodeKind.Editor) != 0) tags.Add(ChecksTagPrefix + "editor");
            if ((checks & CodeKind.Tests) != 0) tags.Add(ChecksTagPrefix + "tests");
            if (!string.IsNullOrEmpty(sourceRule)) tags.Add(SourceRuleTagPrefix + sourceRule);
            tags.AddRange(extraTags);

            return new DiagnosticDescriptor(
                id, title, messageFormat, area, severity, isEnabledByDefault: true,
                description: description, customTags: tags.ToArray());
        }

        public static CodeKind GetChecks(DiagnosticDescriptor descriptor)
        {
            CodeKind checks = CodeKind.None;
            foreach (string tag in descriptor.CustomTags)
            {
                if (!tag.StartsWith(ChecksTagPrefix, StringComparison.Ordinal)) continue;
                switch (tag.Substring(ChecksTagPrefix.Length))
                {
                    case "runtime": checks |= CodeKind.Runtime; break;
                    case "editor": checks |= CodeKind.Editor; break;
                    case "tests": checks |= CodeKind.Tests; break;
                }
            }
            return checks;
        }

        // Null when the rule is new and has no source rule.
        public static string GetSourceRule(DiagnosticDescriptor descriptor)
        {
            foreach (string tag in descriptor.CustomTags)
            {
                if (tag.StartsWith(SourceRuleTagPrefix, StringComparison.Ordinal)) return tag.Substring(SourceRuleTagPrefix.Length);
            }
            return null;
        }
    }
}