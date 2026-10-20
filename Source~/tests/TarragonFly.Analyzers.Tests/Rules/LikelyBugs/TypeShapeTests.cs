using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2043 foreach casts, TF2052 unconstructible classes, TF2055 expanding generic bases,
    // TF2057 [Pure] void methods, TF2065 accessors using the wrong field.
    public class TypeShapeTests
    {
        [Test]
        public void ForeachWithAnExplicitCast_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new ForeachHiddenCastAnalyzer(), @"
using System.Collections;
using System.Collections.Generic;

class Enemy { }
class Goblin : Enemy { }

class Spawner
{
    void Run(List<Enemy> enemies, List<Goblin> goblins, ArrayList untyped, int[] numbers, IEnumerable<object> objects)
    {
        foreach ({|TF2043:Goblin|} goblin in enemies) { }
        foreach ({|TF2043:byte|} number in numbers) { }
        foreach (Enemy enemy in goblins) { }
        foreach (Goblin goblin in untyped) { }
        foreach (Goblin goblin in objects) { }
        foreach (long number in numbers) { }
        foreach (var enemy in enemies) { }
    }
}");
        }

        [Test]
        public void ClassNothingCanCreate_IsReported()
        {
            AnalyzerVerifier.Verify(new UnconstructibleClassAnalyzer(), @"
class {|TF2052:Locked|}
{
    private Locked() { }
    public void Run() { }
}

abstract class {|TF2052:LockedBase|}
{
    private LockedBase() { }
}

class Factory
{
    private Factory() { }
    public static Factory Create() => new Factory();
}

class Constants
{
    private Constants() { }
    public static int Count;
}

class WithBuilder
{
    private WithBuilder() { }
    public class Builder { }
}

class Mixed
{
    private Mixed() { }
    public Mixed(int value) { }
}

class {|TF2052:FindsNothing|}
{
    private FindsNothing() { }
    public int Value;
    public static FindsNothing Find() => null;
}

class {|TF2052:Empty|}
{
    private Empty() { }
}

class Spawner : UnityEngine.MonoBehaviour
{
    private Spawner() { }
    public void Spawn() { }
}

static class Tools { }");
        }

        [Test]
        public void ExpandingGenericBase_IsReported()
        {
            AnalyzerVerifier.Verify(new ExpandingGenericBaseAnalyzer(), @"
class Base<T> { }
class {|TF2055:Node|}<T> : Base<Node<Node<T>>> { }
class {|TF2055:Grid|}<T> : Base<Grid<T[]>> { }
class SelfReferencing<T> : Base<SelfReferencing<T>> { }
class Pair<T, U> : Base<Pair<U, T>> { }
class Twin<T, U> : Base<Twin<T, T>> { }
class Closed<T> : Base<Closed<int>> { }
class Plain : Base<int> { }");
        }

        [Test]
        public void PureMethodReturningNothing_IsReportedOnTheAttribute()
        {
            AnalyzerVerifier.Verify(new PureVoidMethodAnalyzer(), @"
using System.Diagnostics.Contracts;

class Calculator
{
    int total;

    [{|TF2057:Pure|}]
    public void Add(int a) { total += a; }

    [{|TF2057:System.Diagnostics.Contracts.Pure|}]
    public void Reset() { }

    [Pure]
    public int Sum(int a, int b) => a + b;

    [Pure]
    public void Read(out int result) { result = total; }
}");
        }

        [Test]
        public void AccessorUsingAnotherField_IsReported()
        {
            AnalyzerVerifier.Verify(new AccessorUsesWrongFieldAnalyzer(), @"
class Player
{
    int health;
    int armor;
    int _speed;
    int m_level;
    int score;

    public int Health
    {
        get { return {|TF2065:armor|}; }
        set { health = value; }
    }

    public int Armor
    {
        get => armor;
        set => {|TF2065:health|} = value;
    }

    public int Score => {|TF2065:health|};

    public int Speed { get => _speed; set => _speed = value; }
    public int Level { get { return m_level; } }
    public int Total => health + armor;
    public int Other => score;
}

class Inventory
{
    System.Collections.Generic.List<int> items = new System.Collections.Generic.List<int>();
    int[] array = new int[1];

    public int[] Array => {|TF2065:items|}.ToArray();
    public int Count => array.Length;
}");
        }

        [Test]
        public void FieldOfAnotherTypeOrStaticness_IsNotTheExpectedOne()
        {
            AnalyzerVerifier.Verify(new AccessorUsesWrongFieldAnalyzer(), @"
using System.Collections.Generic;

class Hand
{
    private readonly List<int> cards = new List<int>();
    private readonly IReadOnlyList<int> readOnlyCards;
    private static bool shown;
    private bool visible;
    private uint layers;
    private long layersMask;

    public Hand() { readOnlyCards = cards.AsReadOnly(); }

    public IReadOnlyList<int> Cards => readOnlyCards;
    public bool Shown => visible;
    public long Layers { get => layersMask; set => layersMask = value; }
}");
        }

        [Test]
        public void AccessorsGoingThroughAnotherField_AreReported()
        {
            AnalyzerVerifier.Verify(new AccessorUsesWrongFieldAnalyzer(), @"
class SlotData
{
    public bool flag;
}

class Slot
{
    internal SlotData data = new SlotData();
    internal bool flag;

    public bool Flag { {|TF2065:get|} { return data.flag; } set { {|TF2065:data|}.flag = value; } }
}

class Cell
{
    internal SlotData data = new SlotData();
    internal bool flag;

    public bool Flag { get => flag; set => this.{|TF2065:data|}.flag = value; }
}");
        }
    }
}