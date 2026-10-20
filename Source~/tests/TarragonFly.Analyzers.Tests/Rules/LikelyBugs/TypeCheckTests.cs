using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2009 impossible interface cast, TF2036 readonly field copy changed, TF2037 generic compared
    // with null, TF2038 ReferenceEquals on a value type, TF2040 type test on this, TF2046 object's
    // GetHashCode or Equals in an override, TF2048 base.Equals as a same-object check, TF2050 GetType
    // on a Type, TF2058 GetType compared with a nullable type.
    public class TypeCheckTests
    {
        [Test]
        public void CastToInterfaceNoSubclassImplements_IsReported()
        {
            AnalyzerVerifier.Verify(new ImpossibleInterfaceCastAnalyzer(), @"
interface IDamageable { }
interface IFlyer { }
class Pickup { }
class Enemy { }
class FlyingEnemy : Enemy, IFlyer { }

class Target
{
    void Check(Pickup pickup, Enemy enemy, object o, System.Random random)
    {
        IDamageable r1 = ({|TF2009:IDamageable|})pickup;
        IFlyer r2 = (IFlyer)enemy;
        IDamageable r3 = (IDamageable)o;
        IDamageable r4 = (IDamageable)random;
    }
}");
        }

        [Test]
        public void MemberSetThroughReadonlyGenericField_IsReported()
        {
            AnalyzerVerifier.Verify(new ReadonlyFieldCopyChangedAnalyzer(), @"
interface IStats { int Health { get; set; } }

class Holder<T> where T : IStats
{
    readonly T stats;
    T writable;

    Holder(T value)
    {
        stats = value;
        stats.Health = 1;
    }

    void Check()
    {
        {|TF2036:stats.Health|} = 5;
        {|TF2036:stats.Health|} += 1;
        writable.Health = 5;
    }
}

class ClassHolder<T> where T : class, IStats
{
    readonly T stats;

    void Check() { stats.Health = 5; }
}

class StatsAsset : UnityEngine.ScriptableObject, IStats
{
    public int Health { get; set; }
}

class AssetHolder<T> where T : StatsAsset
{
    readonly T stats;

    void Check() { stats.Health = 5; }
}");
        }

        [Test]
        public void AnyChangeThroughReadonlyGenericField_IsReported()
        {
            AnalyzerVerifier.Verify(new ReadonlyFieldCopyChangedAnalyzer(), @"
using System;

interface IStats
{
    int Health { get; set; }
    int Armor { get; set; }
    string Name { get; set; }
    event Action Changed;
}

class Holder<T> where T : IStats
{
    static readonly T Shared;
    readonly T stats;

    static Holder()
    {
        Shared.Health = 1;
        Action reset = () => {|TF2036:Shared.Health|} = 0;
    }

    Holder(T value, Holder<T> other)
    {
        stats = value;
        stats.Health++;
        this.stats.Health = 2;
        {|TF2036:other.stats.Health|} = 2;
        {|TF2036:Shared.Health|} = 3;
        Action heal = () => {|TF2036:this.stats.Health|} = 2;
        Action later = delegate { {|TF2036:stats.Health|} = 1; };
        void Hurt() { {|TF2036:stats.Health|} -= 1; }
    }

    int Value
    {
        get => 0;
        init { stats.Health = value; }
    }

    Holder<T> Other { get; init; }

    int OtherValue
    {
        get => 0;
        init { {|TF2036:Other.stats.Health|} = value; }
    }

    void Check(Action handler)
    {
        {|TF2036:stats.Health|} *= 2;
        {|TF2036:stats.Health|} |= 1;
        {|TF2036:stats.Name|} ??= ""x"";
        {|TF2036:stats.Health|}++;
        --{|TF2036:stats.Health|};
        ({|TF2036:stats.Health|}, {|TF2036:stats.Armor|}) = (1, 2);
        {|TF2036:stats.Changed|} += handler;
    }
}");
        }

        [Test]
        public void UnconstrainedGenericComparedWithNull_IsReported()
        {
            AnalyzerVerifier.Verify(new GenericComparedWithNullAnalyzer(), @"
class Inventory<T>
{
    bool Check(T item) => item == {|TF2037:null|};
    bool Reverse(T item) => {|TF2037:null|} != item;
    bool Pattern(T item) => item is null;
}

class Comparable<T> where T : System.IComparable
{
    bool Check(T item) => item == {|TF2037:null|};
}

class Classes<T> where T : class
{
    bool Check(T item) => item == null;
}

class Structs<T> where T : struct
{
    bool Check(T? item) => item == null;
}

class Assets<T> where T : UnityEngine.ScriptableObject
{
    bool Check(T asset) => asset != null;
}");
        }

        [Test]
        public void ReferenceEqualsWithStruct_IsReported()
        {
            AnalyzerVerifier.Verify(new ReferenceEqualsOnValueTypeAnalyzer(), @"
using UnityEngine;

class Target<T, S> where S : struct
{
    void Check(Vector3 a, Vector3 b, object o, T t, S s, int? n)
    {
        bool r1 = {|TF2038:object.ReferenceEquals|}(a, b);
        bool r2 = {|TF2038:ReferenceEquals|}(o, s);
        bool r3 = ReferenceEquals(o, t);
        bool r4 = ReferenceEquals(o, n);
        bool r5 = ReferenceEquals(o, o);
    }
}");
        }

        [Test]
        public void TypeTestOnThis_IsReportedInGameCode()
        {
            const string Code = @"
class Enemy
{
    void Check()
    {
        if ({|TF2040:this is Boss|}) { }
        if ({|TF2040:this is Boss boss|}) { }
        Boss other = this as Boss;
    }
}

class Boss : Enemy { }";
            AnalyzerVerifier.Verify(new TypeTestOnThisAnalyzer(), Code);
            AnalyzerVerifier.Verify(new TypeTestOnThisAnalyzer(), Code.Replace("{|TF2040:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void ObjectsEqualityInValueEquality_IsReported()
        {
            AnalyzerVerifier.Verify(new BaseObjectHashCodeAnalyzer(), @"
class Direct
{
    int value;

    public override int GetHashCode() => {|TF2046:base.GetHashCode()|};
    public override bool Equals(object obj) => obj is Direct && {|TF2046:base.Equals(obj)|};
}

class Plain { }

class Child : Plain
{
    public override bool Equals(object obj) => {|TF2046:base.Equals(obj)|};
    public override int GetHashCode() => 1;
}

class Valued
{
    public override bool Equals(object obj) => obj is Valued;
    public override int GetHashCode() => 1;
}

class FromValued : Valued
{
    public override bool Equals(object obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
}

class Shortcut
{
    int health;

    public override bool Equals(object other)
    {
        if (base.Equals(other)) return true;
        return other is Shortcut shortcut && shortcut.health == health;
    }

    public override int GetHashCode() => health;
    public int Hash() => base.GetHashCode();
}

struct Value
{
    public override int GetHashCode() => base.GetHashCode();
}");
        }

        [Test]
        public void BaseEqualsShortcutOverFieldComparingBase_IsReported()
        {
            AnalyzerVerifier.Verify(new BaseEqualsAsIdentityCheckAnalyzer(), @"
class Unit
{
    public override bool Equals(object obj) => obj is Unit;
    public override int GetHashCode() => 1;
}

class Soldier : Unit
{
    int rank;

    public override bool Equals(object obj)
    {
        if ({|TF2048:base.Equals(obj)|}) return true;
        return obj is Soldier soldier && soldier.rank == rank;
    }

    public override int GetHashCode() => rank;
}

class Sniper : Unit
{
    public override bool Equals(object obj)
    {
        if ({|TF2048:base.Equals(obj)|})
        {
            return true;
        }

        return false;
    }

    public override int GetHashCode() => 2;
}

class Medic : Unit
{
    int level;

    public override bool Equals(object obj) => base.Equals(obj) && obj is Medic medic && medic.level == level;
    public override int GetHashCode() => level;
}

class Root
{
    public override bool Equals(object obj)
    {
        if (base.Equals(obj)) return true;
        return false;
    }

    public override int GetHashCode() => 1;
}");
        }

        [Test]
        public void GetTypeOnAType_IsReportedFromTheDot()
        {
            AnalyzerVerifier.Verify(new GetTypeOnTypeAnalyzer(), @"
using System;

class Target
{
    void Check(object component, Type type)
    {
        Type r1 = typeof(Target){|TF2050:.GetType()|};
        Type r2 = component.GetType(){|TF2050:.GetType()|};
        Type r3 = type{|TF2050:.GetType()|};
        Type r4 = component.GetType();
    }
}");
        }

        [Test]
        public void GetTypeComparedWithNullableType_IsReported()
        {
            AnalyzerVerifier.Verify(new GetTypeComparedWithNullableAnalyzer(), @"
class Target
{
    void Check(object value, int? n)
    {
        bool r1 = {|TF2058:value.GetType() == typeof(int?)|};
        bool r2 = {|TF2058:typeof(System.Nullable<float>) != value.GetType()|};
        bool r3 = value.GetType() == typeof(int);
    }
}");
        }

        [Test]
        public void BareGetTypeCounts_ButOwnGetTypeMethodsAndThisInATypeDoNot()
        {
            AnalyzerVerifier.Verify(new GetTypeComparedWithNullableAnalyzer(), @"
using System;

struct Cell
{
    bool Check() => {|TF2058:GetType() == typeof(Cell?)|};
}

class Holder
{
    public new Type GetType() => typeof(int?);

    bool Check(Holder other) => other.GetType() == typeof(int?);
}");
            AnalyzerVerifier.Verify(new GetTypeOnTypeAnalyzer(), @"
using System;
using System.Reflection;

class Wrapped : TypeDelegator
{
    public Wrapped() : base(typeof(int)) { }

    public override string ToString() => this.GetType().Name;
}

class Kind
{
    public Type Value = typeof(int);

    public new Kind GetType() => this;
}

class Target
{
    Kind Check(Kind kind) => kind.GetType();
}");
        }
    }
}