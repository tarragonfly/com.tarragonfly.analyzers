# tarragon.fly Analyzers

Roslyn analyzers for Unity games built on Init(args), UniTask and Odin Inspector.

This is still early. The package has the groundwork (settings, assembly scoping, framework checks
and tests), the settings check TF0000, the 46 Unity rules forked from Microsoft.Unity.Analyzers
1.28.0, 164 general rules for readability, likely bugs, dead code, exceptions, async code,
performance and tests, three rules written from the NetAnalyzers rules CA1068, CA1806 and CA1861,
and 20 rules for the mistakes Unity, UniTask, Odin Inspector and Init(args) projects tend to make.

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

The Checks column says which code a rule looks at: all of it, game code (runtime and editor
assemblies), runtime code only or tests. Only code under `Assets/` gets reported by default, so third-party packages
stay quiet.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF0000 | Fix the analyzer settings file | all | |
| TF1000 | File is too long | all | |
| TF1001 | Too many parameters | all | |
| TF1002 | Commented-out code | all | |
| TF1003 | Control flow nested too deeply | all | |
| TF1004 | Method is too long | game | |
| TF1005 | Parameter renamed from the base declaration | all | |
| TF1006 | If statements can be merged | all | |
| TF1007 | Condition is too complex | all | |
| TF1008 | Local variable hides a member | all | |
| TF1009 | Assignment inside an expression | all | |
| TF1010 | Switch section is too long | game | |
| TF1011 | Override only calls the base member | all | |
| TF1012 | Field used only as a local | game | |
| TF1013 | Nested switch statement | all | |
| TF1014 | Branches with identical code | game | |
| TF1015 | Statement looks like part of an unbraced body | all | |
| TF1016 | Field could be readonly | all | |
| TF1017 | Nested type member hides an outer static member | all | |
| TF1018 | Nested ternary operator | all | |
| TF1019 | Method too complex to follow | all | |
| TF1020 | Type in the global namespace | all | |
| TF1021 | If on the same line as a closing brace | all | |
| TF1022 | Unbraced body not indented | all | |
| TF1023 | Overloads are not together | all | |
| TF1024 | Methods with identical bodies | all | |
| TF1500 | Unused private member | all | |
| TF1501 | Unused local variable | all | |
| TF1502 | Value stored but never read | all | |
| TF1503 | Unused type parameter | all | |
| TF1504 | Catch that only rethrows | game | |
| TF1505 | StringBuilder filled but never read | game | |
| TF1506 | Private field never read | all | |
| TF2000 | Empty statement | all | |
| TF2001 | Equals without GetHashCode | game | |
| TF2002 | Parameter or catch variable overwritten before use | all | |
| TF2003 | Floating-point values compared exactly | all | |
| TF2004 | Variable assigned to itself | all | |
| TF2005 | Loop that runs at most once | all | |
| TF2006 | Identical expressions on both sides | game | |
| TF2007 | Object created and dropped | game | |
| TF2008 | Repeated else-if condition | all | |
| TF2009 | Cast that always fails | all | |
| TF2010 | For loop updates the wrong variable | all | |
| TF2011 | Collection passed to its own method | all | |
| TF2012 | Increment whose result is lost | all | |
| TF2013 | & or | where && or || was meant | game | |
| TF2014 | Shift by zero or past the bit width | all | |
| TF2015 | Integer division stored in a floating-point value | all | |
| TF2016 | Endless loop or recursion | all | |
| TF2017 | Remainder compared with a non-zero value | all | |
| TF2018 | Comparison that is always true or false | game | |
| TF2019 | Result of a call without side effects is ignored | game | |
| TF2020 | ToString returns null | game | |
| TF2021 | Arguments passed in swapped order | all | |
| TF2022 | For loop counter moves the wrong way | all | |
| TF2023 | For loop that never runs | all | |
| TF2024 | Composite format string that throws | all | |
| TF2025 | Enumerable.Sum inside unchecked | game | |
| TF2026 | GetHashCode reads a mutable field | all | |
| TF2027 | [Flags] enum members without explicit values | all | |
| TF2028 | [Flags] enum zero value not named None | all | |
| TF2029 | Bit operation with no effect | all | |
| TF2030 | Invisible character in a string | game | |
| TF2031 | Stream.Read length ignored | game | |
| TF2032 | Comparison with NaN | all | |
| TF2033 | IndexOf(...) > 0 misses index 0 | all | |
| TF2034 | Reversed compound operator | all | |
| TF2035 | Doubled ! or ~ operator | all | |
| TF2036 | Change made to a copy of a readonly field | all | |
| TF2037 | Unconstrained generic compared with null | all | |
| TF2038 | ReferenceEquals on a value type | all | |
| TF2039 | Returning an object the using disposes | all | |
| TF2040 | Type test on this | game | |
| TF2041 | Second OrderBy discards the first sort | all | |
| TF2042 | Subtracting a combined delegate | all | |
| TF2043 | Foreach with a hidden cast | all | |
| TF2044 | Call picks the params overload by accident | all | |
| TF2045 | Setter ignores value | all | |
| TF2046 | object's GetHashCode or Equals used in an override | all | |
| TF2047 | Bitwise operation on a non-[Flags] enum | all | |
| TF2048 | base.Equals used as a same-object check | all | |
| TF2049 | Default value hidden by another overload | all | |
| TF2050 | GetType() on a System.Type | all | |
| TF2051 | Shift count that isn't an int | all | |
| TF2052 | Class nothing can construct | all | |
| TF2053 | Format argument no placeholder uses | all | |
| TF2054 | Field never assigned | all | |
| TF2055 | Recursive generic inheritance | game | |
| TF2056 | Base call leaves out an optional argument | all | |
| TF2057 | [Pure] method returning void | all | |
| TF2058 | GetType() compared with a nullable type | all | |
| TF2059 | Every branch has the same code | all | |
| TF2060 | Argument exception without the right parameter name | all | |
| TF2061 | Count comparison that is always the same | all | |
| TF2062 | Exception created but never thrown | all | |
| TF2063 | [Flags] on an enum that isn't flags | all | |
| TF2064 | Collection element overwritten without being read | all | |
| TF2065 | Accessor uses a different field than expected | all | |
| TF2066 | Empty Guid that looks like a new one | all | |
| TF2067 | Regular expression that doesn't parse | all | |
| TF2068 | Name 'field' inside a property accessor | all | |
| TF2069 | Type named 'extension' | game | |
| TF2070 | Return type named 'partial' | all | |
| TF2071 | 'scoped' in lambda parameters | game | |
| TF2100 | Result of a TryParse call is ignored | all | CA1806 |
| TF2500 | Default parameter value differs from the base declaration | all | |
| TF2501 | Overridable method called in a constructor | game | |
| TF2502 | Virtual field-like event | all | |
| TF2503 | Field hides a base class field | game | |
| TF2504 | Dispose that isn't IDisposable.Dispose | game | |
| TF2505 | Lambda unsubscribed from an event | all | |
| TF2506 | Event never raised | all | |
| TF2507 | Custom attribute without AttributeUsage | all | |
| TF2508 | Hiding member is less visible than the base member | all | |
| TF2509 | Method accidentally hides a base method | all | |
| TF2510 | Field differs from a base class field only by case | all | |
| TF3000 | Console output in a Unity game | game | |
| TF3001 | Empty block | all | |
| TF3002 | Null returned instead of an empty collection | game | |
| TF3003 | Catching NullReferenceException | game | |
| TF3004 | Caught exception ignored | game | |
| TF3050 | Throw inside finally | game | |
| TF3051 | Rethrow resets the stack trace | game | |
| TF3052 | Exception thrown from a member that shouldn't throw | all | |
| TF3053 | Argument check in an iterator runs late | all | |
| TF3100 | Log message without a tag | game | |
| TF3101 | Throw in a scenario command | game | |
| TF3500 | Text operation that depends on the player's culture | game | |
| TF3501 | Date or time parsed without a culture | all | |
| TF4001 | Empty Unity message | all | UNT0001 |
| TF4002 | Inefficient tag comparison | all | UNT0002 |
| TF4003 | Usage of non generic GetComponent | all | UNT0003 |
| TF4004 | Time.fixedDeltaTime used with Update | all | UNT0004 |
| TF4005 | Time.deltaTime used with FixedUpdate (off by default) | all | UNT0005 |
| TF4006 | Incorrect message signature | all | UNT0006 |
| TF4007 | Null coalescing on Unity objects | all | UNT0007 |
| TF4008 | Null propagation on Unity objects | all | UNT0008 |
| TF4009 | Missing static constructor with InitializeOnLoad | all | UNT0009 |
| TF4010 | Component instance creation | all | UNT0010 |
| TF4011 | ScriptableObject instance creation | all | UNT0011 |
| TF4012 | Unused Unity Coroutine | all | UNT0012 |
| TF4013 | Remove invalid SerializeField attribute | all | UNT0013 |
| TF4014 | Invalid type for call to GetComponent | all | UNT0014 |
| TF4015 | Use a static and parameterless method | all | UNT0015 |
| TF4016 | Unsafe way to get the method name | all | UNT0016 |
| TF4017 | SetPixels invocation is slow | all | UNT0017 |
| TF4018 | System.Reflection features in performance critical messages | all | UNT0018 |
| TF4019 | Indirection call | all | UNT0019 |
| TF4020 | Make method static | all | UNT0020 |
| TF4021 | Prefer protected Unity Message. (off by default) | all | UNT0021 |
| TF4022 | Inefficient position/rotation assignment | all | UNT0022 |
| TF4023 | Coalescing assignment on Unity objects | all | UNT0023 |
| TF4024 | Give priority to scalar calculations over vector calculations | all | UNT0024 |
| TF4025 | Input.GetKey overloads with KeyCode argument | all | UNT0025 |
| TF4026 | GetComponent always allocates | all | UNT0026 |
| TF4027 | Do not call PropertyDrawer.OnGUI() | all | UNT0027 |
| TF4028 | Use non-allocating physics APIs | all | UNT0028 |
| TF4029 | Pattern matching with null on Unity objects | all | UNT0029 |
| TF4030 | Calling Destroy or DestroyImmediate on a Transform | all | UNT0030 |
| TF4031 | Asset operations in LoadAttribute method | all | UNT0031 |
| TF4032 | Inefficient localPosition/localRotation assignment | all | UNT0032 |
| TF4033 | Incorrect message case | all | UNT0033 |
| TF4034 | A Vector3 can be converted into a Vector2. | all | UNT0034 |
| TF4035 | A Vector3 can be converted into a Vector2. | all | UNT0035 |
| TF4036 | Inefficient position/rotation read | all | UNT0036 |
| TF4037 | Inefficient localPosition/localRotation read | all | UNT0037 |
| TF4038 | WaitForSeconds caching | all | UNT0038 |
| TF4039 | Use RequireComponent attribute when self-invoking GetComponent | all | UNT0039 |
| TF4040 | GameObject.isStatic is editor-only | all | UNT0040 |
| TF4041 | Use Animator.StringToHash instead of strings | all | UNT0041 |
| TF4042 | Mesh array property accessed in loop | all | UNT0042 |
| TF4043 | Possible typo in conditional compilation symbol | all | UNT0043 |
| TF4044 | Avoid temporary strings when setting TextMeshPro text | all | UNT0044 |
| TF4045 | Use non-allocating array access | all | UNT0045 |
| TF4046 | Cache Shader.PropertyToID calls | all | UNT0046 |
| TF4100 | New lambda unregistered from a callback | all | |
| TF4101 | Serialized field of a type Unity can't save (quiet on 6000.6 and newer) | all | |
| TF4102 | Scene search fills in a missing Inspector reference | game | |
| TF5000 | Disposable object never disposed | all | |
| TF5001 | Field disposed outside Dispose | game | |
| TF5002 | Async void method | all | |
| TF5003 | Blocking wait on a task | game | |
| TF5004 | Null returned instead of a Task | all | |
| TF5005 | CancellationToken not passed on | all | |
| TF5100 | UniTask awaited more than once | all | |
| TF5101 | CancellationToken parameter is not last | all | CA1068 |
| TF5102 | Async suffix missing or misplaced | all | |
| TF5103 | MonoBehaviour async method awaits without a cancellation token | all | |
| TF5104 | Async lambda used as a void delegate | all | |
| TF6000 | Odin attribute names a member that doesn't exist | all | |
| TF6001 | Odin attribute names a member of the wrong kind | all | |
| TF6500 | Service creates a new instance without its Inspector values | all | |
| TF6501 | Scene-loaded service without DontDestroyOnLoad = false | all | |
| TF6502 | Scene service created lazily or asynchronously (info) | all | |
| TF6503 | Service<T>.Instance used in a MonoBehaviour | all | |
| TF6504 | Any<T>.Value used in a component | all | |
| TF6505 | Work started from Init(...) | all | |
| TF6506 | Service registered by hand | all | |
| TF7000 | Non-private static field that isn't constant | game | |
| TF7001 | Mutable public static collection | game | |
| TF7002 | Instance member writes a static field | game | |
| TF7003 | Static field in a generic type | all | |
| TF7004 | Static field written in an instance constructor | game | |
| TF7005 | Static field read before it is initialized | all | |
| TF7100 | Static value not reset when Play Mode starts | runtime | |
| TF7101 | Static event not cleared when Play Mode starts | runtime | |
| TF8000 | Count() used to test for any items | game | |
| TF8001 | Empty method | all | |
| TF8002 | String built with + in a loop | game | |
| TF8003 | Property copies a collection on every read | game | |
| TF8004 | LINQ chain with an unneeded step | game | |
| TF8005 | ToCharArray only to loop over or index a string | game | |
| TF8006 | Array built by hand for a params parameter | game | |
| TF8007 | Equals(T) without IEquatable<T> | all | |
| TF8008 | Substring followed by IndexOf | game | |
| TF8009 | DateTime.Now used to measure time | all | |
| TF8010 | FirstOrDefault where Find works | game | |
| TF8011 | All where TrueForAll works | game | |
| TF8012 | Any where Exists works | game | |
| TF8013 | OrderBy before Where | game | |
| TF8014 | First, Last or ElementAt on an indexable list | game | |
| TF8015 | LINQ Min or Max on a SortedSet | all | |
| TF8016 | String overload of StartsWith or EndsWith for one character | game | |
| TF8017 | LINQ First or Last on a LinkedList | all | |
| TF8018 | Any used for an equality check | game | |
| TF8100 | Constant array built on every call | game | CA1861 |
| TF9000 | Ignored test without an end date | tests | |
| TF9001 | Test fixture with no tests | tests | |
| TF9002 | Test without an assertion | tests | |
| TF9003 | Literal true or false in an assertion | tests | |
| TF9004 | Thread.Sleep in a test | tests | |
| TF9005 | Expected and actual swapped in an assertion | tests | |
| TF9006 | Test method the runner can't run | tests | |

The TF1000 to TF9006 rules know about Unity: Unity messages, serialized fields, Odin attributes,
coroutines and UniTask count where they matter, and each rule's source file says how.

The TF4xxx rules keep the upstream number (UNT0008 is TF4008) and behave the same, with one
change: TF4039 counts a `[RequireComponent]` inherited from a base class, which UNT0039 missed. Code
fixes and the upstream suppressors aren't included, since Unity's compiler never runs them.

Two rules need project settings: TF3101 checks nothing until `command_base_types` names a base
class, and TF6506 reports every `Service.Set` call outside the types in
`allowed_service_set_types`. TF4101 stays quiet on Unity 6000.6 and newer, which can save dictionaries
and check serialized fields themselves.

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
| `log_prefix` | `[{Type}]` | Tag TF3100 expects at the start of log messages. Any name without spaces can stand in for `{Type}`, and the warning suggests the calling class. Without `{Type}`, messages have to start with it exactly |
| `command_base_types` | none | Full names of base classes whose code reports failures instead of throwing (TF3101), such as a scenario command base |
| `allowed_service_set_types` | none | Full names of types allowed to call Init(args)' `Service.Set` (TF6506) |

Test code is decided by name. Unity gives editor and test assemblies the same references and
defines, so there's nothing else to go on. An assembly whose name matches `test_assemblies` is
tests. Otherwise it's editor code if the name matches `editor_assemblies` or it references
`UnityEngine.TestRunner`, which Unity adds to every editor-only assembly once the Test Framework
package is installed. Everything else is runtime code.

## Working on the rules

You need the .NET 8 SDK. From a Unity project with the package under `Packages/`:

```powershell
# Build and run the tests.
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1

# See every analyzer warning in the project without opening Unity.
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1
```

Every rule's source file opens with a comment covering what it reports, why it matters in a Unity
game, the framework cases it handles and, for a forked Unity rule, how it differs from the original. Tests mark the
expected warnings inline with `{|TF1000:...|}`.

The Unity rules in `Source~/src/TarragonFly.Analyzers/Rules/Unity/` are forked files and keep
upstream's code style, so later upstream changes are easier to bring over. Their tests compile
against a real editor's assemblies: they find one through `UNITY_EDITOR_PATH` (Unity.exe or its
Editor folder) or Unity Hub's install folders, and get skipped when there's no editor.

| Folder | Contents |
|---|---|
| `Analyzers/` | The built `TarragonFly.Analyzers.dll` that Unity loads |
| `Source~/` | The solution: analyzer (`src/`), NUnit tests (`tests/`) and stubs |
| `Tools~/` | `Analyze.ps1`, `Build.ps1`, `Check.ps1` |

Unity skips folders ending in `~`, so none of the sources or tools get compiled or imported.

## License

MIT, see [LICENSE.md](LICENSE.md). Anything copied from other projects is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
