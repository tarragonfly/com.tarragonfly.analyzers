// Stub of NUnit, compiled as nunit.framework, for snippets compiled as test code.
using System;
using System.Collections;

namespace NUnit.Framework
{
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class TestFixtureAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestAttribute : Attribute
    {
        public object ExpectedResult { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute
    {
        public TestCaseAttribute(params object[] arguments) { }
        public string TestName { get; set; }
        public object ExpectedResult { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseSourceAttribute : Attribute
    {
        public TestCaseSourceAttribute(string sourceName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TearDownAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OneTimeSetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class IgnoreAttribute : Attribute
    {
        public IgnoreAttribute(string reason) { }
        public string Until { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class ExplicitAttribute : Attribute
    {
        public ExplicitAttribute() { }
        public ExplicitAttribute(string reason) { }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CategoryAttribute : Attribute
    {
        public CategoryAttribute(string name) { }
    }

    public delegate void TestDelegate();

    public static class Assert
    {
        public static void That(bool condition) { }
        public static void That(bool condition, string message) { }
        public static void That(object actual, Constraints.IResolveConstraint expression) { }
        public static void That(object actual, Constraints.IResolveConstraint expression, string message) { }
        public static void AreEqual(object expected, object actual) { }
        public static void AreEqual(object expected, object actual, string message) { }
        public static void AreEqual(double expected, double actual, double delta) { }
        public static void AreNotEqual(object expected, object actual) { }
        public static void AreSame(object expected, object actual) { }
        public static void IsTrue(bool condition) { }
        public static void IsFalse(bool condition) { }
        public static void IsNull(object value) { }
        public static void IsNotNull(object value) { }
        public static void Pass() { }
        public static void Fail(string message) { }
        public static void Ignore(string message) { }
        public static T Throws<T>(TestDelegate code) where T : Exception => null;
        public static void DoesNotThrow(TestDelegate code) { }
    }

    public static class Is
    {
        public static Constraints.EqualConstraint EqualTo(object expected) => null;
        public static Constraints.Constraint True => null;
        public static Constraints.Constraint False => null;
        public static Constraints.Constraint Null => null;
        public static Constraints.Constraint Empty => null;
        public static Constraints.ConstraintExpression Not => null;
        public static Constraints.Constraint GreaterThan(object expected) => null;
        public static Constraints.Constraint SameAs(object expected) => null;
    }

    public static class Has
    {
        public static Constraints.ConstraintExpression Length => null;
        public static Constraints.ConstraintExpression Count => null;
        public static Constraints.Constraint Member(object expected) => null;
    }

    public static class CollectionAssert
    {
        public static void AreEqual(IEnumerable expected, IEnumerable actual) { }
        public static void Contains(IEnumerable collection, object actual) { }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual) { }
    }
}

namespace NUnit.Framework.Constraints
{
    public interface IResolveConstraint
    {
    }

    public class Constraint : IResolveConstraint
    {
    }

    public sealed class EqualConstraint : Constraint
    {
        public EqualConstraint Within(double amount) => this;
    }

    public sealed class ConstraintExpression : IResolveConstraint
    {
        public EqualConstraint EqualTo(object expected) => null;
        public Constraint Null => null;
    }
}