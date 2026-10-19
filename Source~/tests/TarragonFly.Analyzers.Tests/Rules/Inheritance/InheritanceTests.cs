using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Inheritance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Inheritance
{
    public class InheritanceTests
    {
        [Test]
        public void ChangedDefaults_AreReported()
        {
            AnalyzerVerifier.Verify(new ChangedDefaultParameterAnalyzer(), @"
interface IPlayer
{
    void Jump(float height = 1f);
    void Run(float speed = 2f);
}

class Base
{
    public virtual void Move(int speed = 1) { }
    public virtual void Turn(int angle = 90) { }
    public virtual void Stop(int fade) { }
    public virtual void Shoot(int count = 1) { }
}

class Derived : Base, IPlayer
{
    public override void Move(int speed = {|TF2500:2|}) { }
    public override void Turn(int angle = 90) { }
    public override void Stop(int fade {|TF2500:= 0|}) { }
    public override void Shoot(int {|TF2500:count|}) { }
    public void Jump(float height = {|TF2500:2f|}) { }
    public void Run(float speed = 2f) { }
}

class Explicit : IPlayer
{
    void IPlayer.Jump(float height {|TF2500:= 1f|}) { }
    void IPlayer.Run(float speed) { }
}");
        }

        [Test]
        public void VirtualFieldLikeEvent_IsReportedOnVirtual()
        {
            AnalyzerVerifier.Verify(new VirtualFieldLikeEventAnalyzer(), @"
using System;

abstract class Spawner
{
    public {|TF2502:virtual|} event Action Died;
    public event Action Spawned;
    public abstract event Action Cleared;
    public virtual event Action Custom { add { } remove { } }
}");
        }

        [Test]
        public void FieldHidingABaseField_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldShadowsBaseFieldAnalyzer(), @"
class Base
{
    protected int health;
    internal int level;
    public int armor;
    private int secret;
    protected static int count;
}

class Derived : Base
{
    protected int {|TF2503:health|};
    private int {|TF2503:level|};
    public new int armor;
    private int secret;
    protected static int count;
}

class GrandChild : Derived
{
    private int {|TF2503:health|};
}");
        }

        [Test]
        public void FieldHidingABaseField_InTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new FieldShadowsBaseFieldAnalyzer(), "class Base { protected int health; } class Derived : Base { protected int health; }", SnippetAssembly.Tests());
        }

        [Test]
        public void DisposeLookAlikes_AreReported()
        {
            AnalyzerVerifier.Verify(new DisposeWithoutIDisposableAnalyzer(), @"
using System;

class Pool
{
    public void {|TF2504:Dispose|}() { }
    public void {|TF2504:Dispose|}(int count) { }
}

struct Handle
{
    public void {|TF2504:Dispose|}() { }
}

interface IOwnDispose
{
    void {|TF2504:Dispose|}();
}

class Resource : IDisposable
{
    public void Dispose() { Dispose(true); }
    protected virtual void Dispose(bool disposing) { }
}

class Derived : Resource
{
    protected override void Dispose(bool disposing) { }
}

class Owned : IOwnDispose
{
    public void Dispose() { }
}

ref struct PooledSpan
{
    public void Dispose() { }
}");
        }

        [Test]
        public void AttributeWithoutUsage_IsReported_UnlessInherited()
        {
            AnalyzerVerifier.Verify(new AttributeWithoutUsageAnalyzer(), @"
using System;

class {|TF2507:ShowInStatsAttribute|} : Attribute { }

[AttributeUsage(AttributeTargets.Field)]
class StatAttribute : Attribute { }

class HealthStatAttribute : StatAttribute { }

abstract class BaseAttribute : Attribute { }

class LayerFieldAttribute : UnityEngine.PropertyAttribute { }");
        }

        [Test]
        public void LessVisibleHidingMember_IsReported()
        {
            AnalyzerVerifier.Verify(new HidingLowersVisibilityAnalyzer(), @"
class Parent
{
    public void Run() { }
    public void Jump(int height) { }
    protected internal void Crouch() { }
    public int Speed { get; set; }
}

class Child : Parent
{
    private void {|TF2508:Run|}() { }
    internal void {|TF2508:Jump|}(int height) { }
    protected void Crouch() { }
    private int {|TF2508:Speed|} { get; set; }
}

class Sibling : Parent
{
    private new void Run() { }
    private void Jump(string height) { }
}");
        }

        [Test]
        public void MethodTakingCallsMeantForTheBase_IsReported()
        {
            AnalyzerVerifier.Verify(new MethodHidesBaseMethodAnalyzer(), @"
class Animal { }
class Dog : Animal { }

class Base
{
    public void Feed(Dog dog) { }
    public void Pet(Animal animal) { }
    public void Walk(Dog dog, int minutes) { }
    private void Hidden(Dog dog) { }
}

class Derived : Base
{
    public void {|TF2509:Feed|}(Animal animal) { }
    public void Pet(Dog dog) { }
    public void Walk(object animal, long minutes) { }
    public void Hidden(Animal animal) { }
    public void Feed(Animal animal, int amount) { }
}");
        }

        [Test]
        public void FieldDifferingOnlyByCase_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldDiffersByCaseAnalyzer(), @"
class Base
{
    protected int health;
    public int Armor;
    private int secret;
    protected static int count;
    protected int Speed { get; set; }
}

class Derived : Base
{
    private int {|TF2510:Health|};
    private int {|TF2510:armor|};
    private int Secret;
    private int Count;
    private int speed;
}");
        }

        [Test]
        public void OverridableCallInConstructor_IsReported()
        {
            const string Code = @"
using System;

class Enemy
{
    Enemy()
    {
        {|TF2501:SetUpStats|}();
        {|TF2501:this.Spawn|}();
        Prepare();
        Helper();
        Action later = () => SetUpStats();
        void Local() => SetUpStats();
        later();
        int hp = Health;
    }

    protected virtual void SetUpStats() { }
    protected virtual void Spawn() { }
    void Prepare() { }
    static void Helper() { }
    protected virtual int Health => 1;
}

abstract class Unit
{
    protected Unit() { {|TF2501:Load|}(); }
    protected abstract void Load();
}

class Boss : Unit
{
    public Boss() { {|TF2501:Load|}(); }
    protected override void Load() { }
}

class FinalBoss : Unit
{
    public FinalBoss() { Load(); }
    protected sealed override void Load() { }
}

sealed class Sealed
{
    Sealed() { ToString(); }
}";
            AnalyzerVerifier.Verify(new VirtualCallInConstructorAnalyzer(), Code);
            AnalyzerVerifier.Verify(new VirtualCallInConstructorAnalyzer(), Code.Replace("{|TF2501:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void LambdaUnsubscribedFromEvent_IsReportedFromTheOperator()
        {
            AnalyzerVerifier.Verify(new LambdaUnsubscribeAnalyzer(), @"
using System;

class Player
{
    public event Action Died;
    Action stored;

    void Check()
    {
        Died += () => Console.WriteLine();
        Died {|TF2505:-= () => Console.WriteLine()|};
        Died {|TF2505:-= delegate { Console.WriteLine(); }|};
        Died -= OnDied;
        Died -= stored;
        stored -= () => Console.WriteLine();
    }

    void OnDied() { }
}");
        }
    }
}