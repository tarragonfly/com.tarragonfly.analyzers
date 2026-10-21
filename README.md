# tarragon.fly Analyzers

Roslyn analyzers for Unity games. The package itself is `Packages/com.tarragonfly.analyzers`, and its
[README](Packages/com.tarragonfly.analyzers/README.md) has the install steps, the rule list and the
settings keys.

Install it in a game from the Package Manager with this git URL, keeping a release tag after the `#`:

```
https://github.com/tarragonfly/com.tarragonfly.analyzers.git?path=Packages/com.tarragonfly.analyzers#v0.3.1
```

The rest of this repository is an empty Unity 6000.6 project, set up so the package stays embedded
while the rules are being worked on. Open it in Unity, or run
`Packages/com.tarragonfly.analyzers/Tools~/Check.ps1` and `Analyze.ps1` from this folder.
