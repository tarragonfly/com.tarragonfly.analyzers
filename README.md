# tarragon.fly Analyzers

Roslyn analyzers for Unity games built on Init(args), UniTask and Odin Inspector.

This is still early. The package currently has the groundwork (settings, assembly scoping,
framework checks, tests and a parity tool), the settings check TF0000, and the 46 Unity rules forked
from Microsoft.Unity.Analyzers 1.28.0. More rules come next, in
batches.

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

Every rule checks runtime, editor and test code. Only code under `Assets/` gets reported by
default, so third-party packages stay quiet.

| Rule | Title | Replaces |
|---|---|---|
| TF0000 | Fix the analyzer settings file | |
| TF4001 | Empty Unity message | UNT0001 |
| TF4002 | Inefficient tag comparison | UNT0002 |
| TF4003 | Usage of non generic GetComponent | UNT0003 |
| TF4004 | Time.fixedDeltaTime used with Update | UNT0004 |
| TF4005 | Time.deltaTime used with FixedUpdate (off by default) | UNT0005 |
| TF4006 | Incorrect message signature | UNT0006 |
| TF4007 | Null coalescing on Unity objects | UNT0007 |
| TF4008 | Null propagation on Unity objects | UNT0008 |
| TF4009 | Missing static constructor with InitializeOnLoad | UNT0009 |
| TF4010 | Component instance creation | UNT0010 |
| TF4011 | ScriptableObject instance creation | UNT0011 |
| TF4012 | Unused Unity Coroutine | UNT0012 |
| TF4013 | Remove invalid SerializeField attribute | UNT0013 |
| TF4014 | Invalid type for call to GetComponent | UNT0014 |
| TF4015 | Use a static and parameterless method | UNT0015 |
| TF4016 | Unsafe way to get the method name | UNT0016 |
| TF4017 | SetPixels invocation is slow | UNT0017 |
| TF4018 | System.Reflection features in performance critical messages | UNT0018 |
| TF4019 | Indirection call | UNT0019 |
| TF4020 | Make method static | UNT0020 |
| TF4021 | Prefer protected Unity Message. (off by default) | UNT0021 |
| TF4022 | Inefficient position/rotation assignment | UNT0022 |
| TF4023 | Coalescing assignment on Unity objects | UNT0023 |
| TF4024 | Give priority to scalar calculations over vector calculations | UNT0024 |
| TF4025 | Input.GetKey overloads with KeyCode argument | UNT0025 |
| TF4026 | GetComponent always allocates | UNT0026 |
| TF4027 | Do not call PropertyDrawer.OnGUI() | UNT0027 |
| TF4028 | Use non-allocating physics APIs | UNT0028 |
| TF4029 | Pattern matching with null on Unity objects | UNT0029 |
| TF4030 | Calling Destroy or DestroyImmediate on a Transform | UNT0030 |
| TF4031 | Asset operations in LoadAttribute method | UNT0031 |
| TF4032 | Inefficient localPosition/localRotation assignment | UNT0032 |
| TF4033 | Incorrect message case | UNT0033 |
| TF4034 | A Vector3 can be converted into a Vector2. | UNT0034 |
| TF4035 | A Vector3 can be converted into a Vector2. | UNT0035 |
| TF4036 | Inefficient position/rotation read | UNT0036 |
| TF4037 | Inefficient localPosition/localRotation read | UNT0037 |
| TF4038 | WaitForSeconds caching | UNT0038 |
| TF4039 | Use RequireComponent attribute when self-invoking GetComponent | UNT0039 |
| TF4040 | GameObject.isStatic is editor-only | UNT0040 |
| TF4041 | Use Animator.StringToHash instead of strings | UNT0041 |
| TF4042 | Mesh array property accessed in loop | UNT0042 |
| TF4043 | Possible typo in conditional compilation symbol | UNT0043 |
| TF4044 | Avoid temporary strings when setting TextMeshPro text | UNT0044 |
| TF4045 | Use non-allocating array access | UNT0045 |
| TF4046 | Cache Shader.PropertyToID calls | UNT0046 |

The TF4xxx rules keep the upstream number (UNT0008 is TF4008) and behave the same, with one
change: TF4039 counts a `[RequireComponent]` inherited from a base class, which UNT0039 missed. Code
fixes and the upstream suppressors aren't included, since Unity's compiler never runs them.

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

The Unity rules in `Source~/src/TarragonFly.Analyzers/Rules/Unity/` are forked files and keep
upstream's code style, so later upstream changes are easier to bring over. Their tests compile
against a real editor's assemblies: they find one through `UNITY_EDITOR_PATH` (Unity.exe or its
Editor folder) or Unity Hub's install folders, and get skipped when there's no editor.

| Folder | Contents |
|---|---|
| `Analyzers/` | The built `TarragonFly.Analyzers.dll` that Unity loads |
| `Source~/` | The solution: analyzer (`src/`), NUnit tests (`tests/`), stubs, parity tool and corpus |
| `Tools~/` | `Analyze.ps1`, `Build.ps1`, `Check.ps1` |

Unity skips folders ending in `~`, so none of the sources or tools get compiled or imported.

## License

MIT, see [LICENSE.md](LICENSE.md). Anything copied from other projects is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
