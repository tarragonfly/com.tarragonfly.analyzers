// Stub of NUnit, compiled as nunit.framework, for snippets compiled as test code.
using System;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute
    {
        public TestCaseAttribute(params object[] arguments) { }
    }

    public static class Assert
    {
        public static void That(bool condition) { }
        public static void AreEqual(object expected, object actual) { }
    }
}