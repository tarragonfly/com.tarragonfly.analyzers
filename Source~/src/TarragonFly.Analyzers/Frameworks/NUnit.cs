using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // NUnit and the Unity Test Framework attributes on top of it.
    public static class NUnit
    {
        private static readonly string[] TestAttributes =
        {
            "NUnit.Framework.TestAttribute",
            "NUnit.Framework.TestCaseAttribute",
            "NUnit.Framework.TestCaseSourceAttribute",
            "NUnit.Framework.TheoryAttribute",
            UnityTestAttribute
        };

        public const string UnityTestAttribute = "UnityEngine.TestTools.UnityTestAttribute";

        // A method the test runner runs: [Test], [TestCase], [TestCaseSource], [Theory] or [UnityTest].
        public static bool IsTest(IMethodSymbol method)
        {
            foreach (string attribute in TestAttributes)
            {
                if (SymbolNames.HasAttribute(method, attribute)) return true;
            }
            return false;
        }

        public static bool IsUnityTest(IMethodSymbol method)
        {
            return SymbolNames.HasAttribute(method, UnityTestAttribute);
        }

        public static bool IsFixture(INamedTypeSymbol type)
        {
            return SymbolNames.HasAttribute(type, "NUnit.Framework.TestFixtureAttribute");
        }

        // Test methods declared on the type or inherited from its base classes.
        public static bool HasTests(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                foreach (ISymbol member in current.GetMembers())
                {
                    if (member is IMethodSymbol method && IsTest(method)) return true;
                }
            }
            return false;
        }
    }
}