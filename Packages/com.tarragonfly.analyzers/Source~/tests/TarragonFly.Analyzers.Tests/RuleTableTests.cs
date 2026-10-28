using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace TarragonFly.Analyzers.Tests
{
    // Every rule the analyzer ships, checked against each other and against the README's rule table.
    public class RuleTableTests
    {
        [Test]
        public void RuleIds_AreUniqueAcrossAnalyzers()
        {
            IEnumerable<string> shared = Analyzers()
                .SelectMany(analyzer => analyzer.SupportedDiagnostics.Select(rule => (rule.Id, Analyzer: analyzer.GetType().Name)))
                .GroupBy(rule => rule.Id)
                .Where(group => group.Select(rule => rule.Analyzer).Distinct().Count() > 1)
                .Select(group => group.Key + ": " + string.Join(", ", group.Select(rule => rule.Analyzer).Distinct()));

            Assert.That(shared, Is.Empty, "Rule IDs reported by more than one analyzer.");
        }

        [Test]
        public void EveryRule_HasATitleAndAMessage()
        {
            IEnumerable<string> blank = Rules()
                .Where(rule => string.IsNullOrWhiteSpace(rule.Title.ToString()) || string.IsNullOrWhiteSpace(rule.MessageFormat.ToString()))
                .Select(rule => rule.Id);

            Assert.That(blank, Is.Empty, "Rules with an empty title or message.");
        }

        // The README row is "| ID | Title | Checks | Replaces |"; the descriptor is the source of truth.
        [Test]
        public void ReadmeTable_MatchesEveryDescriptor()
        {
            Dictionary<string, string> rules = Rules().ToDictionary(rule => rule.Id, RowText);
            Dictionary<string, string> rows = ReadmeRows();

            List<string> differences = new List<string>();
            foreach (string id in rules.Keys.Union(rows.Keys).OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!rows.TryGetValue(id, out string row)) differences.Add($"{id} is missing from the README: {rules[id]}");
                else if (!rules.TryGetValue(id, out string rule)) differences.Add($"{id} is in the README but no analyzer reports it");
                else if (rule != row) differences.Add($"{id} README: {row}\n{id} rule:   {rule}");
            }

            Assert.That(differences, Is.Empty, string.Join("\n", differences));
        }

        private static string RowText(DiagnosticDescriptor rule)
        {
            return $"{rule.Title} | {ChecksWord(Descriptors.GetChecks(rule))} | {Descriptors.GetSourceRule(rule) ?? string.Empty}";
        }

        // The words the README's Checks column uses.
        private static string ChecksWord(CodeKind checks)
        {
            switch (checks)
            {
                case CodeKind.All: return "all";
                case CodeKind.Game: return "game";
                case CodeKind.Runtime: return "runtime";
                case CodeKind.Tests: return "tests";
                case CodeKind.Runtime | CodeKind.Tests: return "runtime and tests";
                default: return $"(no README word for {checks} yet; add one to ChecksWord)";
            }
        }

        private static Dictionary<string, string> ReadmeRows()
        {
            string readme = Path.Combine(TestContext.CurrentContext.TestDirectory, "README.md");
            Regex row = new Regex(@"^\| (TF\d{4}) \| (.*) \| (\w+(?: and \w+)?) \|\s*(\w*)\s*\|$");
            Dictionary<string, string> rows = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(readme))
            {
                Match match = row.Match(line);
                if (!match.Success) continue;
                // A README title may end with a note the descriptor doesn't carry, such as "(off by default)".
                string title = Regex.Replace(match.Groups[2].Value.Replace(@"\|", "|"), @" \([^)]*\)$", string.Empty);
                rows.Add(match.Groups[1].Value, $"{title} | {match.Groups[3].Value} | {match.Groups[4].Value}");
            }
            Assert.That(rows, Is.Not.Empty, "No rule rows found in " + readme);
            return rows;
        }

        private static IEnumerable<DiagnosticDescriptor> Rules()
        {
            return Analyzers().SelectMany(analyzer => analyzer.SupportedDiagnostics).GroupBy(rule => rule.Id).Select(group => group.First());
        }

        private static IEnumerable<DiagnosticAnalyzer> Analyzers()
        {
            return typeof(ScopedAnalyzer).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type));
        }
    }
}