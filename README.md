# tarragon.fly Analyzers

Roslyn analyzers for Unity 6.0 and later (tested on 6.3, 6.6 and 6.7), aware of Init(args), UniTask
and Odin Inspector. The DLL in `Analyzers/` is labelled `RoslynAnalyzer`, so Unity runs it on every
assembly in the project, Rider lists it in its generated project files, and Project Auditor shows
its warnings. Severities are set per assembly in its `.ruleset`, like any other analyzer.

## Installing

Add the package to `Packages/manifest.json` from git, pinned to a release tag:

```json
"com.tarragonfly.analyzers": "https://github.com/tarragonfly/com.tarragonfly.analyzers.git#v0.1.0"
```

To work on the rules from a game project, clone it into `Packages/com.tarragonfly.analyzers`
instead (for example as a git submodule); Unity then uses it as an embedded package.

## Rules

| Rule | Title | Checks |
|---|---|---|
| TF0000 | Fix the analyzer settings file | runtime, editor, tests |

Only code under `Assets/` is reported by default, so third-party packages stay quiet.

## Settings

Put a file named `Settings.TarragonFly.Analyzers.additionalfile` anywhere under `Assets/`. Unity
passes files named `Name.[AnalyzerName].additionalfile` to the analyzer with that assembly name, in
every assembly, and Rider's project files list it too. Where that does not work (reported for some
6000.0 patches), add a `csc.rsp` next to an asmdef with the line
`-additionalfile:Assets/Settings.TarragonFly.Analyzers.additionalfile`.

The file is `key = value` lines; lines starting with `#` are comments and lists are comma separated.
Every key has a default, so a missing file or key changes nothing. Lines the analyzer cannot use are
reported as TF0000.

| Key | Default | Meaning |
|---|---|---|
| `analyze_paths` | `Assets/` | Folders (or single `.cs` files) whose code is reported, relative to the project |
| `test_assemblies` | `*.Tests, *.Test` | Assembly names that hold tests; `*` matches anything, case is ignored |
| `editor_assemblies` | `*.Editor, *.Editor.*, *-Editor, *-Editor-*` | Assembly names that hold editor code |
| `max_parameters` | 7 | Limits, each a whole number of 1 or more |
| `max_method_lines` | 80 | |
| `max_file_lines` | 1000 | |
| `max_nesting` | 3 | |
| `max_condition_operators` | 3 | |
| `max_switch_section_statements` | 8 | |
| `max_cognitive_complexity` | 15 | |
| `log_prefix` | `[{Type}]` | Prefix expected at the start of log messages; `{Type}` is the calling class |
| `command_base_types` | none | Full names of base classes for scenario commands |
| `allowed_service_set_types` | none | Full names of types allowed to register Init(args) services at runtime |

An assembly is tests if its name matches `test_assemblies`, else editor code if its name matches
`editor_assemblies` or it references `UnityEngine.TestRunner` (Unity adds that reference to every
editor-only assembly when the Test Framework package is installed), else runtime code. Names decide
for tests because Unity gives editor and test assemblies the same references and defines.

## Working on the rules

Needs the .NET 8 SDK. From a Unity project that has the package under `Packages/`:

```powershell
# Build, test, and compare the rules with Microsoft.Unity.Analyzers.
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1

# Every analyzer warning in the project without opening Unity.
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1
```

From a standalone clone, pass the game project: `Tools~/Check.ps1 -ProjectPath <project>`.

Each rule's source file starts with a comment saying what it reports, why it matters in a Unity
game, the framework cases it handles and any intended differences from the rule it replaces. Tests
mark expected warnings inline with `{|TF1000:...|}`.

| Folder | Contents |
|---|---|
| `Analyzers/` | The built `TarragonFly.Analyzers.dll` that Unity loads |
| `Source~/` | Solution: analyzer (`src/`), NUnit tests (`tests/`), stubs, parity tool and corpus |
| `Tools~/` | `Analyze.ps1`, `Build.ps1`, `Check.ps1` |

Unity ignores folders ending in `~`, so the sources and tools are never compiled or imported.

## License

MIT, see [LICENSE.md](LICENSE.md). Code copied from other projects is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
