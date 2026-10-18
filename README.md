# tarragon.fly Analyzers

Roslyn analyzers for Unity games built on Init(args), UniTask and Odin Inspector.

This is still early. The package currently ships the groundwork (settings, assembly scoping,
framework checks, tests and a parity tool) and one rule, TF0000. Rules that replace the
Microsoft.Unity.Analyzers checks we use get added in batches, each one compared against the rule it
replaces before it's switched on.

It works on Unity 6.0 and later, and we've tested it on 6.3, 6.6 and 6.7. The DLL in `Analyzers/`
carries the `RoslynAnalyzer` label, so Unity runs it on every assembly, Rider picks it up from its
generated project files and Project Auditor lists its warnings. Severities live in each assembly's
`.ruleset`, same as any other analyzer.

## Installing

Add it to `Packages/manifest.json`, pinned to a release tag:

```json
"com.tarragonfly.analyzers": "https://github.com/tarragonfly/com.tarragonfly.analyzers.git#v0.1.0"
```

If you want to work on the rules inside a game project, clone it into
`Packages/com.tarragonfly.analyzers` instead, for example as a git submodule. Unity then treats it
as an embedded package and you can edit it in place.

## Rules

| Rule | Title | Checks |
|---|---|---|
| TF0000 | Fix the analyzer settings file | runtime, editor, tests |

Only code under `Assets/` gets reported by default, so third-party packages stay quiet.

## Settings

Settings go in a file named `Settings.TarragonFly.Analyzers.additionalfile`, anywhere under
`Assets/`. Unity hands files named `Name.[AnalyzerName].additionalfile` to the matching analyzer in
every assembly, and Rider's project files list it too. Some 6000.0 patches have a bug with this; on
those, add a `csc.rsp` next to an asmdef with the line
`-additionalfile:Assets/Settings.TarragonFly.Analyzers.additionalfile`.

Each line is `key = value`. Lines starting with `#` are comments, and lists are comma separated.
Every key has a default, so leaving the file out changes nothing. A line the analyzer can't use,
like a misspelled key, shows up as TF0000.

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

Test code is decided by name. Unity gives editor and test assemblies the same references and
defines, so there's nothing else to go on. An assembly whose name matches `test_assemblies` is
tests. Otherwise it's editor code if the name matches `editor_assemblies` or it references
`UnityEngine.TestRunner`, which Unity adds to every editor-only assembly once the Test Framework
package is installed. Everything else is runtime code.

## Working on the rules

You need the .NET 8 SDK. From a Unity project with the package under `Packages/`:

```powershell
# Build, run the tests, and compare the rules with Microsoft.Unity.Analyzers.
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1

# See every analyzer warning in the project without opening Unity.
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1
```

From a standalone clone, point it at a game project with `Tools~/Check.ps1 -ProjectPath <project>`.

Every rule's source file opens with a comment covering what it reports, why it matters in a Unity
game, the framework cases it handles and how it differs from the rule it replaces. Tests mark the
expected warnings inline with `{|TF1000:...|}`.

| Folder | Contents |
|---|---|
| `Analyzers/` | The built `TarragonFly.Analyzers.dll` that Unity loads |
| `Source~/` | The solution: analyzer (`src/`), NUnit tests (`tests/`), stubs, parity tool and corpus |
| `Tools~/` | `Analyze.ps1`, `Build.ps1`, `Check.ps1` |

Unity skips folders ending in `~`, so none of the sources or tools get compiled or imported.

## License

MIT, see [LICENSE.md](LICENSE.md). Anything copied from other projects is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
