using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Settings
{
    // The values from Assets/Settings.TarragonFly.Analyzers.additionalfile. Every key has a default,
    // so a missing file or a missing key changes nothing. The package README lists the keys.
    public sealed class AnalyzerSettings
    {
        // Unity passes "Filename.[AnalyzerName].additionalfile" files to the analyzer with that assembly name.
        public const string FileNameSuffix = ".TarragonFly.Analyzers.additionalfile";

        public static readonly AnalyzerSettings Defaults = new AnalyzerSettings(null);

        private static readonly ConditionalWeakTable<AdditionalText, AnalyzerSettings> ParsedByFile = new ConditionalWeakTable<AdditionalText, AnalyzerSettings>();

        private readonly List<SettingsProblem> problems = new List<SettingsProblem>();

        private AnalyzerSettings(AdditionalText file)
        {
            File = file;
        }

        // Null when the project has no settings file.
        public AdditionalText File { get; }
        public ImmutableArray<SettingsProblem> Problems => problems.ToImmutableArray();

        public int MaxParameters { get; private set; } = 7;
        public int MaxMethodLines { get; private set; } = 80;
        public int MaxFileLines { get; private set; } = 1000;
        public int MaxNesting { get; private set; } = 3;
        public int MaxConditionOperators { get; private set; } = 4;
        public int MaxSwitchSectionStatements { get; private set; } = 8;
        public int MaxCognitiveComplexity { get; private set; } = 15;

        // Files outside these folders, such as Packages/ and Library/PackageCache/, are never reported.
        public ImmutableArray<string> AnalyzePaths { get; private set; } = ImmutableArray.Create("Assets/");

        // Unity references UnityEngine.TestRunner and nunit from every editor-only assembly, so test
        // assemblies can only be told apart by name.
        public ImmutableArray<string> TestAssemblies { get; private set; } = ImmutableArray.Create("*.Tests", "*.Test");
        public ImmutableArray<string> EditorAssemblies { get; private set; } = ImmutableArray.Create("*.Editor", "*.Editor.*", "*-Editor", "*-Editor-*");

        public string LogPrefix { get; private set; } = "[{Type}]";
        public ImmutableArray<string> AllowedServiceSetTypes { get; private set; } = ImmutableArray<string>.Empty;

        // Which kinds of code TF1025 reports 'var' in: none (the default), game or all.
        public CodeKind ReportVarIn { get; private set; } = CodeKind.None;

        // TF1026 is silent until the project lists words. Entries are matched as whole name tokens,
        // so "flux capacitor" matches fluxCapacitor and FluxCapacitorId but not fluxCapacitors.
        public ImmutableArray<string> BannedNameWords { get; private set; } = ImmutableArray<string>.Empty;
        public ImmutableArray<string> BannedMethodPrefixes { get; private set; } = ImmutableArray<string>.Empty;
        public bool BannedNameWordsSkipSerialized { get; private set; } = true;

        // TF1027 is off until ascii_only is set; non_ascii_allowed_in lists project-relative path globs it leaves alone.
        public bool AsciiOnly { get; private set; }
        public ImmutableArray<string> NonAsciiAllowedIn { get; private set; } = ImmutableArray<string>.Empty;

        // TF1029 is silent until the project lists members; discouraged_members_allowed_in names the
        // types (the helpers themselves) that may still call them directly.
        public ImmutableArray<DiscouragedMember> DiscouragedMembers { get; private set; } = ImmutableArray<DiscouragedMember>.Empty;
        public ImmutableArray<string> DiscouragedMembersAllowedIn { get; private set; } = ImmutableArray<string>.Empty;

        // Test assemblies that hold the shared fakes and helpers, where TF9008 lets reflection through.
        public ImmutableArray<string> TestSupportAssemblies { get; private set; } = ImmutableArray<string>.Empty;

        public static bool IsSettingsFile(AdditionalText file)
        {
            return Path.GetFileName(file.Path).EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase);
        }

        // The first settings file wins; the same file passed twice (additionalfile and csc.rsp) is fine.
        public static AnalyzerSettings Read(AnalyzerOptions options, CancellationToken ct)
        {
            foreach (AdditionalText file in options.AdditionalFiles)
            {
                if (IsSettingsFile(file)) return Read(file, ct);
            }
            return Defaults;
        }

        public static AnalyzerSettings Read(AdditionalText file, CancellationToken ct)
        {
            return ParsedByFile.GetValue(file, f => Parse(f, ct));
        }

        private static AnalyzerSettings Parse(AdditionalText file, CancellationToken ct)
        {
            AnalyzerSettings settings = new AnalyzerSettings(file);
            SourceText text = file.GetText(ct);
            if (text == null)
            {
                settings.problems.Add(new SettingsProblem(default, "The settings file could not be read."));
                return settings;
            }

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextLine line in text.Lines)
            {
                string content = line.ToString();
                string trimmed = content.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                TextSpan lineSpan = TrimmedSpan(line, content);
                int equals = trimmed.IndexOf('=');
                if (equals <= 0)
                {
                    settings.problems.Add(new SettingsProblem(lineSpan, $"Expected 'key = value', found '{trimmed}'."));
                    continue;
                }

                string key = trimmed.Substring(0, equals).Trim();
                string value = trimmed.Substring(equals + 1).Trim();
                string error = settings.Apply(key, value);
                if (error != null) settings.problems.Add(new SettingsProblem(lineSpan, error));
                else if (!seenKeys.Add(key)) settings.problems.Add(new SettingsProblem(lineSpan, $"'{key}' is set more than once; the last value is used."));
            }
            return settings;
        }

        private static TextSpan TrimmedSpan(TextLine line, string content)
        {
            int start = content.Length - content.TrimStart().Length;
            int length = content.Trim().Length;
            return new TextSpan(line.Start + start, length);
        }

        // Returns an error message, or null when the value was applied.
        private string Apply(string key, string value)
        {
            switch (key)
            {
                case "max_parameters": return ApplyLimit(key, value, v => MaxParameters = v);
                case "max_method_lines": return ApplyLimit(key, value, v => MaxMethodLines = v);
                case "max_file_lines": return ApplyLimit(key, value, v => MaxFileLines = v);
                case "max_nesting": return ApplyLimit(key, value, v => MaxNesting = v);
                case "max_condition_operators": return ApplyLimit(key, value, v => MaxConditionOperators = v);
                case "max_switch_section_statements": return ApplyLimit(key, value, v => MaxSwitchSectionStatements = v);
                case "max_cognitive_complexity": return ApplyLimit(key, value, v => MaxCognitiveComplexity = v);
                case "analyze_paths": AnalyzePaths = SplitList(value); return null;
                case "test_assemblies": TestAssemblies = SplitList(value); return null;
                case "editor_assemblies": EditorAssemblies = SplitList(value); return null;
                case "log_prefix": LogPrefix = value; return null;
                case "allowed_service_set_types": AllowedServiceSetTypes = SplitList(value); return null;
                case "report_var": return ApplyCodeKind(key, value, v => ReportVarIn = v);
                case "banned_name_words": BannedNameWords = SplitList(value); return null;
                case "banned_method_prefixes": BannedMethodPrefixes = SplitList(value); return null;
                case "banned_name_words_skip_serialized": return ApplyFlag(key, value, v => BannedNameWordsSkipSerialized = v);
                case "ascii_only": return ApplyFlag(key, value, v => AsciiOnly = v);
                case "non_ascii_allowed_in": NonAsciiAllowedIn = SplitList(value); return null;
                case "discouraged_members": return ApplyDiscouragedMembers(value);
                case "discouraged_members_allowed_in": DiscouragedMembersAllowedIn = SplitList(value); return null;
                case "test_support_assemblies": TestSupportAssemblies = SplitList(value); return null;
                default: return $"Unknown setting '{key}'. The package README lists the keys.";
            }
        }

        private static string ApplyLimit(string key, string value, Action<int> set)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) || limit < 1)
            {
                return $"'{key}' needs a whole number of 1 or more, found '{value}'.";
            }
            set(limit);
            return null;
        }

        private static string ApplyFlag(string key, string value, Action<bool> set)
        {
            if (!bool.TryParse(value, out bool flag)) return $"'{key}' needs true or false, found '{value}'.";
            set(flag);
            return null;
        }

        // "none", "game" (runtime and editor assemblies) or "all" (tests too).
        private static string ApplyCodeKind(string key, string value, Action<CodeKind> set)
        {
            switch (value.ToLowerInvariant())
            {
                case "none": set(CodeKind.None); return null;
                case "game": set(CodeKind.Game); return null;
                case "all": set(CodeKind.All); return null;
                default: return $"'{key}' needs none, game or all, found '{value}'.";
            }
        }

        // A bad entry fails the whole line, so the rule stays off rather than matching half a list.
        private string ApplyDiscouragedMembers(string value)
        {
            ImmutableArray<DiscouragedMember>.Builder members = ImmutableArray.CreateBuilder<DiscouragedMember>();
            foreach (string entry in SplitList(value, keepParenthesized: true))
            {
                string error = DiscouragedMember.TryParse(entry, out DiscouragedMember member);
                if (error != null) return error;
                members.Add(member);
            }
            DiscouragedMembers = members.ToImmutable();
            return null;
        }

        // Comma separated; with keepParenthesized, commas inside parentheses belong to the entry, as in
        // "A.B.M(int, int) -> X, C.D.N -> Y".
        private static ImmutableArray<string> SplitList(string value, bool keepParenthesized = false)
        {
            ImmutableArray<string>.Builder items = ImmutableArray.CreateBuilder<string>();
            int start = 0;
            int depth = 0;
            for (int i = 0; i <= value.Length; i++)
            {
                if (i < value.Length)
                {
                    if (keepParenthesized && value[i] == '(') depth++;
                    else if (keepParenthesized && value[i] == ')' && depth > 0) depth--;
                    if (value[i] != ',' || depth > 0) continue;
                }
                string trimmed = value.Substring(start, i - start).Trim();
                if (trimmed.Length > 0) items.Add(trimmed);
                start = i + 1;
            }
            return items.ToImmutable();
        }
    }
}