using System;

namespace TarragonFly.Analyzers
{
    // Which kind of code an assembly holds, and which kinds a rule checks.
    [Flags]
    public enum CodeKind
    {
        None = 0,
        Runtime = 1,
        Editor = 2,
        Tests = 4,
        Game = Runtime | Editor,
        All = Runtime | Editor | Tests
    }
}