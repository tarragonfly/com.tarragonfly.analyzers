using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class SetterIgnoresValueTests
    {
        [Test]
        public void AccessorNotUsingValue_IsReportedOnItsKeyword()
        {
            AnalyzerVerifier.Verify(new SetterIgnoresValueAnalyzer(), @"
using System;

class Player
{
    int health;
    int[] slots = new int[3];
    event Action changed;

    public int Health { get => health; {|TF2045:set|} { health = 100; } }
    public int Armor { get => health; {|TF2045:set|} => health = 1; }
    public int Empty { get => health; {|TF2045:set|} { } }
    public int Shield { get => health; {|TF2045:init|} { health = 2; } }
    public int this[int i] { get => slots[i]; {|TF2045:set|} => slots[i] = 0; }
    public event Action Changed { {|TF2045:add|} { changed += Log; } remove { changed -= value; } }

    static void Log() { }
}");
        }

        [Test]
        public void UsesThrowsAutoPropertiesAndExplicitImplementations_AreFine()
        {
            AnalyzerVerifier.Verify(new SetterIgnoresValueAnalyzer(), @"
using System;

interface IHealth
{
    int Value { get; set; }
}

class Player : IHealth
{
    int health;
    Func<int> getter;

    public int Health { get => health; set => health = value; }
    public int Lazy { get => health; set { getter = () => value; } }
    public int Named { get => health; set { Console.WriteLine(nameof(value)); } }
    public int ReadOnly { get => health; set => throw new NotSupportedException(); }
    public int Locked { get => health; set { throw new NotSupportedException(); } }
    public int Auto { get; set; }
    int IHealth.Value { get => 0; set { } }
}");
        }
    }
}