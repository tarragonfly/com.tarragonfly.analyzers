// Stub of Unity's test runner, compiled as UnityEngine.TestRunner. Unity references the real one
// from every editor-only assembly, so tests add it to mimic an editor assembly.
using System;

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTestAttribute : Attribute
    {
    }
}