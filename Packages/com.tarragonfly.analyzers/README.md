# Tarragon.Fly() Analyzers

The package is a Roslyn analyzer: a set of checks that Unity's C# compiler runs over your scripts
every time it compiles them. Findings appear where compiler warnings already appear, in the Unity
Console, in Rider and Visual Studio, and in Project Auditor. Nothing runs in Play Mode and nothing
ends up in a build. Each finding carries a rule ID that starts with `TF`, so a line in the Console
looks like this:

```
Assets/Game/Player.cs(42,13): warning TF4008: Unity objects should not use null propagation.
```

The 238 rules fall into two groups:

- **General C# checks that understand Unity.** Likely bugs, dead code, swallowed exceptions,
  hard-to-read methods, allocation-heavy LINQ and weak tests. Where a general-purpose analyzer
  would flag normal Unity code, these rules know better: a Unity message such as `Awake` counts as
  called, a `[SerializeField]` field counts as assigned, a coroutine counts as a method that runs,
  and Odin attributes count as uses of the members they name.
- **Checks for mistakes specific to Unity and its popular add-ons.** The 46 rules of Microsoft's
  Unity analyzers (`?.` on a destroyed object, `GetComponent` with a string, an `Update` with no
  body) plus rules for UniTask, Odin Inspector and Init(args): a `UniTask` awaited twice, an Odin
  `[ShowIf("isOpen")]` naming a member that does not exist, a `[Service]` that will be created
  without its Inspector values, a static field that keeps its value when Play Mode starts without
  a domain reload.

A project that does not use UniTask, Odin Inspector or Init(args) can still use the package. Those
rules look for the frameworks' types by name and have nothing to report when the types are absent.

This is an early release. The rules are tested, but they are still being tuned against real
projects, so expect some of them to get narrower or wider between versions.

## Requirements

- Unity 6000.0 or newer. Tested on 6000.3, 6000.6 and 6000.7.
- git on your PATH, since Unity fetches the package with the `git` command.
- Nothing else to use it. The .NET 8 SDK is only needed to change the rules themselves (see
  [Working on the rules](#working-on-the-rules)).

## Installing

From the Package Manager:

1. Open **Window > Package Manager**.
2. Click **+** at the top left and choose **Install package from git URL...**
3. Paste the URL with a release tag after the `#` and click **Install**:

   ```
   https://github.com/tarragonfly/com.tarragonfly.analyzers.git?path=Packages/com.tarragonfly.analyzers#v1.0.1
   ```

4. Add `TarragonFly.Analyzers.Scope` to the Assembly Definition References of each of your
   `.asmdef` files, so the analyzer runs on them (see
   [What happens after installing](#what-happens-after-installing)).

Or add the same line to `Packages/manifest.json` yourself:

```json
"com.tarragonfly.analyzers": "https://github.com/tarragonfly/com.tarragonfly.analyzers.git?path=Packages/com.tarragonfly.analyzers#v1.0.1"
```

Keep the tag. Without it, Unity locks the package to whatever `main` was when you installed it, so
upgrading is guesswork. With it, you upgrade by changing the tag. Versions are listed under Tags on
GitHub. Tags up to v0.1.3 have the package at the repository root, so they install from the URL
without the `?path=` part.

To work on the rules, clone the repository and open it in Unity. The repository root is an empty
Unity 6000.6 project with the package embedded under `Packages/com.tarragonfly.analyzers`, so the
Package Manager lists it as editable and every `Build.ps1` run shows up there on the next refresh.
To try a build in one of your games before it is released, point the game's manifest at the clone;
`file:` paths are relative to `Packages/`, so for a clone next to the game folder the line is:

```json
"com.tarragonfly.analyzers": "file:../../com.tarragonfly.analyzers/Packages/com.tarragonfly.analyzers"
```

Swap the git URL back in before you commit. An embedded or `file:` copy wins over a git URL entry
with the same name, so keep only one of them.

## What happens after installing

The analyzer DLL in `Analyzers/` carries Unity's `RoslynAnalyzer` label and sits next to
`TarragonFly.Analyzers.Scope.asmdef`, so Unity runs it only on assemblies that reference
`TarragonFly.Analyzers.Scope`, plus the predefined ones (`Assembly-CSharp`,
`Assembly-CSharp-Editor` and their `firstpass` versions), which Unity always gives every analyzer.
Scripts without an `.asmdef` of their own are checked straight away; each `.asmdef` you want checked
needs the reference. The scope assembly holds no code and is not auto-referenced, so nothing else
changes for the assemblies that use it. It's also editor-only. Runtime assemblies can reference it
and it still stays out of player builds, since Unity simply drops the reference when it compiles for
a player. Those `.asmdef` assemblies build without the analyzer there, so player builds get a bit
faster, and their findings still show up in the editor as usual.

Third-party packages and plugins don't load it at all. Changing the settings file or updating the
package only recompiles the assemblies that run it, so compiles stay short.

Findings show up in the Console alongside the compiler's own warnings. Rider picks the analyzer up
from the project files Unity generates, and Project Auditor lists its findings in its code view.

Only code under `Assets/` is reported, even in an assembly that runs the analyzer. Third-party code
that lives inside `Assets/` without an `.asmdef`, such as some asset store imports and samples,
compiles into `Assembly-CSharp` and is reported like your own until you narrow `analyze_paths` (see
[Settings](#settings)) or switch rules off for that assembly (next section).

Upgrading from 0.3 or earlier: those versions ran on every assembly in the project. From 0.4.0 an
`.asmdef` assembly without the `TarragonFly.Analyzers.Scope` reference reports nothing, with no
warning, so add the reference before you rely on a clean Console.

Nearly every rule reports as a warning. Five Unity rules are style or micro-optimisation nudges and
report as infos instead: TF4019, TF4024, TF4034, TF4035 and TF4039. TF6502 is an info too. Two Unity
rules, TF4005 and TF4021, are off until you turn them on.

## Choosing rules and severities

Severities are set with Unity's rule set files, the same way as for any other analyzer. A rule set
is an XML file that lists rule IDs with an action: `None` switches a rule off, `Info`, `Warning` and
`Error` set its severity, and `Error` fails the compile.

```xml
<?xml version="1.0" encoding="utf-8"?>
<RuleSet Name="Game scripts" ToolsVersion="10.0">
  <Rules AnalyzerId="TarragonFly.Analyzers" RuleNamespace="TarragonFly.Analyzers">
    <Rule Id="TF1004" Action="None" />    <!-- long methods are fine here -->
    <Rule Id="TF4008" Action="Error" />   <!-- ?. on a Unity object fails the compile -->
    <Rule Id="TF4005" Action="Warning" /> <!-- on, it is off by default -->
  </Rules>
</RuleSet>
```

Where the file goes decides which assemblies it covers:

| File | Covers |
|---|---|
| `Assets/Default.ruleset` | Every assembly without a rule set of its own, including `Assembly-CSharp` and the assemblies of third-party `.asmdef` files |
| `Assets/Assembly-CSharp.ruleset`, `Assets/Assembly-CSharp-Editor.ruleset` and so on | That predefined assembly |
| A `.ruleset` in the same folder as an `.asmdef` | That assembly |

A common setup is a shared rule set for your own code that each of your `.asmdef` folders includes:

```xml
<Include Path="..\..\GameRules.ruleset" Action="Default" />
```

Keep shared rule sets out of `Assets/Default.ruleset`. Unity passes that file to every assembly,
including third-party ones that never run the analyzer, so each edit to it recompiles all of them.

Unity watches the rule set it reads, not the files that one includes. After editing an included
file, change a script in the assembly or reimport it so Unity compiles again.

To silence one finding instead of a rule, use the compiler's usual pragma around the code:

```csharp
#pragma warning disable TF1004 // one long switch is the clearest way to write this
...
#pragma warning restore TF1004
```

## Which code a rule checks

Each rule declares which kind of code it looks at, shown in the Checks column of the tables below:

| Checks | Meaning |
|---|---|
| all | Every assembly |
| game | Runtime and editor assemblies, not tests |
| runtime | Runtime assemblies only |
| tests | Test assemblies only |

An assembly's kind is decided by name, because Unity gives editor and test assemblies the same
references and defines. An assembly whose name matches `test_assemblies` holds tests. Otherwise it
holds editor code if its name matches `editor_assemblies` or it references `UnityEngine.TestRunner`,
which Unity adds to every editor-only assembly once the Test Framework package is installed.
Everything else is runtime code. Both name patterns are settings.

## Settings

Settings are optional. They live in one plain text file named
`Settings.TarragonFly.Analyzers.additionalfile`, anywhere under `Assets/`. Unity hands files named
`Name.[AnalyzerName].additionalfile` to the matching analyzer in every assembly that runs it, and
Rider's project files list it too.

```
# Report our own scripts only. The rest of Assets/ is plugins and samples.
analyze_paths = Assets/Game

# TF6506: the two views that re-register their services for Enter Play Mode without reloads.
allowed_service_set_types = Game.UI.ViewManager, Game.UI.ConfirmationPopup

# TF1007: a 5-operator condition is the plain way to write a parser check; 6 and up still report.
max_condition_operators = 5

# TF1010: the save and load switches have one case per step and read fine up to 12 statements.
max_switch_section_statements = 12

# TF1025: write the type instead of var in game code; tests may keep var.
report_var = game

# TF1029: views reach their elements through UIViewBase.Element, which warns when one is missing.
discouraged_members = UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element
discouraged_members_allowed_in = UIViewBase
```

Each line is `key = value`. Lines starting with `#` are comments, and lists are comma separated.
Every key has a default, so a missing file or a missing key changes nothing. A line the analyzer
cannot use, such as a misspelled key or a limit that is not a whole number, is reported as TF0000
and the setting keeps its default.

| Key | Default | Meaning |
|---|---|---|
| `analyze_paths` | `Assets/` | Folders (or single `.cs` files) whose code is reported, relative to the project |
| `test_assemblies` | `*.Tests, *.Test` | Assembly names that hold tests; `*` matches anything, case is ignored |
| `editor_assemblies` | `*.Editor, *.Editor.*, *-Editor, *-Editor-*` | Assembly names that hold editor code |
| `max_parameters` | 7 | TF1001. Limits are whole numbers of 1 or more |
| `max_method_lines` | 80 | TF1004 |
| `max_file_lines` | 1000 | TF1000 |
| `max_nesting` | 3 | TF1003 |
| `max_condition_operators` | 4 | TF1007 |
| `max_switch_section_statements` | 8 | TF1010 |
| `max_cognitive_complexity` | 15 | TF1019 |
| `log_prefix` | `[{Type}]` | Tag TF3100 expects at the start of log messages. Any name without spaces can stand in for `{Type}`, and the warning suggests the calling class. Without `{Type}`, messages have to start with it exactly |
| `allowed_service_set_types` | none | Full names of types allowed to call Init(args)' `Service.Set` (TF6506) |
| `report_var` | `none` | TF1025. `game` reports `var` in runtime and editor code, `all` in tests too |
| `banned_name_words` | none | Words TF1026 reports in declared names and file names, matched as whole words of the name: `widget` matches `widgetCount` and `WidgetPool` but not `widgets`; a two-word entry matches its words side by side |
| `banned_method_prefixes` | none | Words TF1026 reports when a method name starts with one, such as `handle` or `process` |
| `banned_name_words_skip_serialized` | `true` | TF1026 leaves out fields Unity saves (`[SerializeField]`, or public on a Unity object or `[Serializable]` type), since renaming one loses the values stored in scenes, prefabs and assets |
| `ascii_only` | `false` | TF1027 reports every character above U+007F in source files |
| `non_ascii_allowed_in` | none | Project-relative path globs TF1027 leaves alone, such as `Assets/Game/Dialogue/*` |
| `discouraged_members` | none | Members TF1029 reports direct calls to, as `Namespace.Type.Member -> Replacement`, one per comma. Every overload counts; a parameter list after the member, `UnityEngine.Debug.Log(object, UnityEngine.Object) -> Log.Info`, picks one, with the types short or with their namespace |
| `discouraged_members_allowed_in` | none | Types, with or without their namespace, whose code may still call those members directly; the helpers themselves go here |

Some Unity 6000.0 patches have a bug that drops additional files. On those, add a `csc.rsp` next
to an `.asmdef` with the line `-additionalfile:Assets/Settings.TarragonFly.Analyzers.additionalfile`.

## Rules

Every rule's source file under `Source~/src/TarragonFly.Analyzers/Rules/` opens with a comment that
gives an example of what it reports, why that matters in a Unity game and which cases it leaves
alone. The tables below are the short version. The Replaces column names the Microsoft rule a TF
rule stands in for, where there is one.

### Settings

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF0000 | Fix the analyzer settings file | all | |

### Hard-to-read code (TF1000s)

Methods, files and conditions that have grown past the limits in the settings, plus layouts that
hide what the code does, such as a statement indented as if it belonged to an unbraced `if`.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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
| TF1025 | 'var' instead of the type name (off by default) | all | |
| TF1026 | Name uses a word the project avoids (needs `banned_name_words`) | all | |
| TF1027 | Non-ASCII character in source (off by default) | all | |
| TF1028 | Suppression without a reason | all | |
| TF1029 | Direct call where the project has a helper (needs `discouraged_members`) | all | |

TF1025 is off until `report_var` is set: `game` reports runtime and editor code, `all` reports tests
too. TF1026 reports nothing until `banned_name_words` or `banned_method_prefixes` lists words, and
leaves fields Unity saves alone while `banned_name_words_skip_serialized` is `true`. TF1027 is off
until `ascii_only = true`; `non_ascii_allowed_in` lists path globs it skips. TF1028 wants a comment
on the line of a `#pragma warning disable` or `[UsedImplicitly]`, or on the line above it, and a
`Justification` on `[SuppressMessage]`. TF1029 reports nothing until `discouraged_members` lists
members; `discouraged_members_allowed_in` names the types that may still call them directly.

### Dead code (TF1500s)

Members, locals and values nothing reads. Unity messages, serialized fields and members named by
Odin attributes are not dead, and these rules know that.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF1500 | Unused private member | all | |
| TF1501 | Unused local variable | all | |
| TF1502 | Value stored but never read | all | |
| TF1503 | Unused type parameter | all | |
| TF1504 | Catch that only rethrows | game | |
| TF1505 | StringBuilder filled but never read | game | |
| TF1506 | Private field never read | all | |

### Likely bugs (TF2000s)

Code that compiles but almost never does what was meant: a loop counter moving the wrong way,
`&` where `&&` was meant, an integer division stored in a float, a regular expression that does not
parse.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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
| TF2013 | & or \| where && or \|\| was meant | game | |
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

### Inheritance and events (TF2500s)

Members that quietly hide, shadow or fail to override what a base class or an event subscriber
expects.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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
| TF2511 | Event subscribed but never unsubscribed | game | |

TF2511 pairs each `+=` or `AddListener` on another object's event with a `-=` or `RemoveListener`
of the same event and handler somewhere in the type, or a `RemoveAllListeners` on that event. A
subscription meant to last for the object's whole life gets a `#pragma warning disable TF2511`
with a reason. Events the type raises itself, lambdas (TF2505 and TF4100), static constructors and
`[RuntimeInitializeOnLoadMethod]` methods are left alone. So are events of the component's own
GameObject or its children, reached through `GetComponent`, `GetComponentInChildren`,
`TryGetComponent`, `AddComponent`, `Transform.Find` or `GetChild`, directly or through locals and
unsaved fields that only get such values, and objects the type creates itself with `new`, such as an
`InputAction` made in `OnEnable`, as long as it isn't a Unity object and is never passed as an argument,
stored, returned or captured by a lambda; a `= null` reset doesn't count against those fields. Test code isn't
checked, since tests create and destroy what they subscribe to. `AsyncOperation.completed` and Addressables'
`AsyncOperationHandle.Completed` fire once, so they're skipped too. `EditorApplication.delayCall` fires
once and Unity clears it before the call, so it is skipped too. UI Toolkit's `Button.clicked` is
skipped like `RegisterCallback`, which isn't covered.

### Silent failures, exceptions, logging and culture (TF3000s)

Places where a problem disappears instead of being seen: a swallowed exception, a `Console.WriteLine`
that no Unity log collects, a log line with no tag to search for, a string comparison or number format
that changes with the player's language.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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
| TF3102 | Number parsed without a culture | all | CA1305 |
| TF3103 | Number or date turned into text without a culture | game | CA1305 |
| TF3104 | Prefix or suffix check that depends on the player's culture | game | CA1310 |
| TF3105 | Character case changed with the player's culture | game | CA1304 |
| TF3500 | Text operation that depends on the player's culture | game | |
| TF3501 | Date or time parsed without a culture | all | |

TF3100 takes its tag shape from the `log_prefix` setting.

### Unity (TF4000s)

TF4001 to TF4046 are the rules of
[Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers) 1.28.0 with new
IDs: UNT0008 is TF4008, and so on. They behave the same, with two changes: TF4039 counts a
`[RequireComponent]` inherited from a base class, which UNT0039 missed, and TF4003 also reports
`GetComponent(typeof(T))` on another object, `AddComponent(typeof(T))`, `CreateInstance(typeof(T))`
and type names passed as constant strings, saying when a name matches no type, since the call then
returns null. UNT0003 only reported `GetComponent(typeof(T))` with no receiver. The rules that are
only about speed, such as TF4038's cached WaitForSeconds, check game code only, like the TF8000s: a
test that waits once or sets a position once doesn't need the faster form. Microsoft's code fixes and
suppressors are not included, since Unity's compiler never runs them. TF4100 to TF4105 are new rules
for Unity API mistakes.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF4001 | Empty Unity message | all | UNT0001 |
| TF4002 | Inefficient tag comparison | all | UNT0002 |
| TF4003 | Non-generic GetComponent, AddComponent or CreateInstance | all | UNT0003 |
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
| TF4017 | SetPixels invocation is slow | game | UNT0017 |
| TF4018 | System.Reflection features in performance critical messages | all | UNT0018 |
| TF4019 | Indirection call | all | UNT0019 |
| TF4020 | Make method static | all | UNT0020 |
| TF4021 | Prefer protected Unity Message. (off by default) | all | UNT0021 |
| TF4022 | Inefficient position/rotation assignment | game | UNT0022 |
| TF4023 | Coalescing assignment on Unity objects | all | UNT0023 |
| TF4024 | Give priority to scalar calculations over vector calculations | game | UNT0024 |
| TF4025 | Input.GetKey overloads with KeyCode argument | all | UNT0025 |
| TF4026 | GetComponent always allocates | game | UNT0026 |
| TF4027 | Do not call PropertyDrawer.OnGUI() | all | UNT0027 |
| TF4028 | Use non-allocating physics APIs | game | UNT0028 |
| TF4029 | Pattern matching with null on Unity objects | all | UNT0029 |
| TF4030 | Calling Destroy or DestroyImmediate on a Transform | all | UNT0030 |
| TF4031 | Asset operations in LoadAttribute method | all | UNT0031 |
| TF4032 | Inefficient localPosition/localRotation assignment | game | UNT0032 |
| TF4033 | Incorrect message case | all | UNT0033 |
| TF4034 | A Vector3 can be converted into a Vector2. | all | UNT0034 |
| TF4035 | A Vector2 can be converted into a Vector3. | all | UNT0035 |
| TF4036 | Inefficient position/rotation read | game | UNT0036 |
| TF4037 | Inefficient localPosition/localRotation read | game | UNT0037 |
| TF4038 | WaitForSeconds caching | game | UNT0038 |
| TF4039 | Use RequireComponent attribute when self-invoking GetComponent | all | UNT0039 |
| TF4040 | GameObject.isStatic is editor-only | all | UNT0040 |
| TF4041 | Use Animator.StringToHash instead of strings | game | UNT0041 |
| TF4042 | Mesh array property accessed in loop | game | UNT0042 |
| TF4043 | Possible typo in conditional compilation symbol | all | UNT0043 |
| TF4044 | Avoid temporary strings when setting TextMeshPro text | game | UNT0044 |
| TF4045 | Use non-allocating array access | game | UNT0045 |
| TF4046 | Cache Shader.PropertyToID calls | game | UNT0046 |
| TF4100 | New lambda unregistered from a callback | all | |
| TF4101 | Serialized field of a type Unity can't save (quiet on 6000.6 and newer) | all | |
| TF4102 | Scene search fills in a missing Inspector reference | game | |
| TF4103 | OnValidate finds or assigns a reference | game | |
| TF4104 | OnValidate changes another Unity object | game | |
| TF4105 | OnValidate creates, destroys or saves objects | game | |

TF4101 stays quiet on Unity 6000.6 and newer, which can save dictionaries and checks serialized
fields itself.

TF4103 to TF4105 keep `OnValidate` to clamping and normalizing the object's own values. Unity runs
it often, while loading and on a loading thread, and the other objects might not have had `Awake`
yet. Three rules, one per kind of fix:

- **TF4103, a reference found or set there.** A Unity object field or property set in `OnValidate`,
  including to null; an array or list of Unity objects replaced, as in `huds = null`, or changed
  with `huds[0] = ...`, `Add`, `Remove`, `RemoveAll` or `Clear`; and `GetComponent`, scene searches and `Resources` or
  `AssetDatabase` loads. A `GetComponent` there finds whatever happens to be loaded, and a field set
  there, or a missing reference quietly removed from a list, hides a broken Inspector assignment
  until a build. Assign the reference in the Inspector or look it up in `Awake`.
- **TF4104, a change to another Unity object.** An assignment to one of its members, a change to one
  of its arrays or lists, or a void call on it, such as `transform.position = ...`,
  `catalog.prefabs[0] = null` or `gameObject.SetActive(false)`. A component's own
  `name = ...` counts too, since that renames the GameObject. Change other objects from an editor
  command or at runtime.
- **TF4105, objects made, destroyed or saved.** `Instantiate`, `PrefabUtility.InstantiatePrefab`,
  `Destroy`, `AddComponent`, `Undo.AddComponent`, `CreateInstance`, `new` of any Unity object such
  as `new Material(...)`, `SetDirty`, `Undo` and asset or scene saves. A ScriptableObject's own
  `name = ...` counts too, since the asset stops matching its file name. That's a job for an editor
  command you run on purpose.

```csharp
void OnValidate()
{
    _id = _id.Trim();                            // fine: the object's own value
    _volume = Mathf.Clamp01(_volume);            // fine
    _renderer = GetComponent<SpriteRenderer>();  // TF4103
    _label.text = "v" + Application.version;     // TF4104
    EditorUtility.SetDirty(this);                // TF4105
}
```

Ints, strings and `[Serializable]` classes are the object's own values and are never reported, and
neither are the object's own `enabled` and `hideFlags`, or a local list used as scratch space. All
three rules also follow the same-file private helpers that `OnValidate` calls or hands to a callback
such as `EditorApplication.delayCall`, and the getters and setters of this type's properties it
reads or writes, such as `Renderer Body => _body ??= GetComponent<Renderer>();`, but not one only
named in `nameof`. `Size += 1` and `Size++` run both the getter and the setter. They look inside
lambdas, since deferred work is still `OnValidate`'s work, and a `?.` call such as
`huds?.RemoveAll(...)` or `catalog?.prefabs.Clear()` counts the same as a plain one.

What they don't report: reading other objects, such as `if (_target == null)` or
`transform.parent`, since checking a reference is what `OnValidate` is for. Subscribing to
`EditorApplication.delayCall` on its own, since a delayed call that checks the object is still
alive is the accepted way to update an editor preview; what the callback does is still checked.
Helpers in another file, a base class or a static utility class aren't followed, since a report
has to land in the file being checked.

### Async code, disposables and UniTask (TF5000s)

Tasks that are awaited twice or never, `async void` methods whose exceptions vanish, cancellation
tokens that stop halfway, and the UniTask naming and parameter conventions.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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
| TF5105 | Cancellation check the await already does | all | |

### Odin Inspector and Init(args) (TF6000s)

Odin attributes take member names as strings, which the compiler cannot check; TF6000 and TF6001
do. Group attributes take their paths as strings too, and a broken path only shows up once someone
opens the object in the Inspector. TF6002 to TF6004 catch the three ways it breaks: a parent group
nothing declares, one group with two group attribute types, and a member in two groups that don't
nest. The TF6500 rules cover the ways an Init(args) `[Service]` ends up wired differently from what the Inspector shows.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF6000 | Odin attribute names a member that doesn't exist | all | |
| TF6001 | Odin attribute names a member of the wrong type | all | |
| TF6002 | Odin group nested in a group nothing declares | all | |
| TF6003 | Odin group declared with different group attributes | all | |
| TF6004 | Member placed in Odin groups that don't nest | all | |
| TF6500 | Service creates a new instance without its Inspector values | all | |
| TF6501 | Scene-loaded service without DontDestroyOnLoad = false | all | |
| TF6502 | Scene service created lazily or asynchronously (info) | all | |
| TF6503 | Service<T>.Instance used in a MonoBehaviour | all | |
| TF6504 | Any<T>.Value used in a component | all | |
| TF6505 | Work started from Init(...) | all | |
| TF6506 | Service registered by hand | all | |

TF6506 reports every `Service.Set` call outside the types listed in `allowed_service_set_types`.

### Static state (TF7000s)

Static fields are the usual reason a game behaves differently on the second Play Mode entry when
domain reload is off. TF7100 and TF7101 check that runtime statics are reset or cleared when Play
Mode starts.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF7000 | Non-private static field that isn't constant | game | |
| TF7001 | Mutable public static collection | game | |
| TF7002 | Instance member writes a static field | game | |
| TF7003 | Static field in a generic type | all | |
| TF7004 | Static field written in an instance constructor | game | |
| TF7005 | Static field read before it is initialized | all | |
| TF7100 | Static value not reset when Play Mode starts | runtime | |
| TF7101 | Static event not cleared when Play Mode starts | runtime | |

### Performance (TF8000s)

Allocations and extra work that show up as garbage collector spikes when the code runs every frame:
LINQ where a `List<T>` method does the same without an iterator, strings built with `+` in a loop,
a constant array rebuilt on every call.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
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

### Tests (TF9000s)

Checked in test assemblies only: tests that cannot fail, cannot run or were switched off and
forgotten.

| Rule | Title | Checks | Replaces |
|---|---|---|---|
| TF9000 | Ignored test without an end date | tests | |
| TF9001 | Test fixture with no tests | tests | |
| TF9002 | Test without an assertion | tests | |
| TF9003 | Literal true or false in an assertion | tests | |
| TF9004 | Thread.Sleep in a test | tests | |
| TF9005 | Expected and actual swapped in an assertion | tests | |
| TF9006 | Test method the runner can't run | tests | |

## Working on the rules

You need the .NET 8 SDK and, for the forked Unity rule tests, a Unity editor. From the root of a
clone, which is the Unity project that holds the package:

```powershell
# Build the analyzer and run its tests.
& Packages/com.tarragonfly.analyzers/Tools~/Check.ps1

# List every analyzer finding in this project without opening Unity.
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1

# The same for one of your games.
& Packages/com.tarragonfly.analyzers/Tools~/Analyze.ps1 -ProjectPath C:\Games\MyGame
```

The project around the package is empty apart from its settings. Drop scripts into `Assets/` when
you want to watch a rule fire in the Console; until there is at least one, `Analyze.ps1` has nothing
to replay there and stops with "No assemblies matched".

| Folder | Contents |
|---|---|
| `Analyzers/` | The built `TarragonFly.Analyzers.dll` that Unity loads |
| `Source~/` | The .NET solution: the analyzer in `src/`, NUnit tests in `tests/`, and stubs of the Unity, UniTask, Odin, Init(args) and NUnit types the tests compile against |
| `Tools~/` | `Build.ps1`, `Check.ps1` and `Analyze.ps1` |

Unity skips folders whose names end in `~`, so the sources and tools are never compiled or imported
into the project; only the DLL in `Analyzers/` is.

`Build.ps1` builds in Release and copies the DLL into `Analyzers/` only when its bytes changed,
because a new DLL makes Unity recompile every assembly in the project. `Check.ps1` runs the build
and then the tests. `Analyze.ps1` replays Unity's last compile with the editor's own compiler and
writes findings as SARIF plus a summary under `Logs/analyzers/`; its header comment lists the
filters (`-Assembly`, `-Rule`, `-Changed`) and exit codes.

Each rule lives in its own file named after its ID, such as `Rules/Performance/TF8002_StringConcatInLoop.cs`,
and the file's opening comment is its documentation. Tests mark the expected findings inline with
`{|TF8002:...|}` around the offending code. A test checks that this README's rule tables match the
descriptors in the DLL, so a new or renamed rule has to be added here before the tests pass.

The forked Unity rules in `Rules/Unity/` keep Microsoft's code style so that later upstream changes
are easier to bring over. Their tests compile against a real editor's assemblies: they find one
through `UNITY_EDITOR_PATH` (Unity.exe or its Editor folder) or Unity Hub's install folders, and
fail when there is none, so a green run always means every test ran.

## License

MIT, see [LICENSE.md](LICENSE.md). Code taken from other projects is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
