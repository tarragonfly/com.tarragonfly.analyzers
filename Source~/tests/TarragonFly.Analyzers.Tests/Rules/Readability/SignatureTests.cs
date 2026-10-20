using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    // TF1001 too many parameters, TF1005 renamed parameters.
    public class SignatureTests
    {
        [Test]
        public void TooManyParameters_IsReportedOnTheParameterList()
        {
            AnalyzerVerifier.Verify(new TooManyParametersAnalyzer(), @"
using System;

delegate void Wide{|TF1001:(int a, int b, int c, int d, int e, int f, int g, int h)|};

class Spawner
{
    public Spawner{|TF1001:(int a, int b, int c, int d, int e, int f, int g, int h)|} { }

    public void Spawn{|TF1001:(int a, int b, int c, int d, int e, int f, int g, int h)|}
    {
        Action<int, int, int, int, int, int, int, int> lambda = {|TF1001:(i1, i2, i3, i4, i5, i6, i7, i8)|} => { };
    }

    public void Seven(int a, int b, int c, int d, int e, int f, int g) { }
}");
        }

        [Test]
        public void FixedSignatures_AreSkipped()
        {
            AnalyzerVerifier.Verify(new TooManyParametersAnalyzer(), @"
interface ISpawner
{
    void Spawn{|TF1001:(int a, int b, int c, int d, int e, int f, int g, int h)|};
}

abstract class SpawnerBase
{
    public abstract void Place{|TF1001:(int a, int b, int c, int d, int e, int f, int g, int h)|};
}

class Spawner : SpawnerBase, ISpawner
{
    public void Spawn(int a, int b, int c, int d, int e, int f, int g, int h) { }
    public override void Place(int a, int b, int c, int d, int e, int f, int g, int h) { }

    [System.Runtime.InteropServices.DllImport(""native"")]
    static extern void Native(int a, int b, int c, int d, int e, int f, int g, int h);
}");
        }

        [Test]
        public void InitArgsInitMethod_IsSkipped()
        {
            AnalyzerVerifier.Verify(new TooManyParametersAnalyzer(), @"
using Sisus.Init;

class Wide : IArgs<int, int>
{
    public void Init(int a, int b) { }
}", SnippetAssembly.WithSettings("max_parameters = 1"));
        }

        [Test]
        public void ParameterLimit_ComesFromSettings()
        {
            AnalyzerVerifier.Verify(new TooManyParametersAnalyzer(), "class Spawner { void Spawn{|TF1001:(int a, int b)|} { } }", SnippetAssembly.WithSettings("max_parameters = 1"));
        }

        [Test]
        public void RenamedParameters_AreReported()
        {
            AnalyzerVerifier.Verify(new RenamedParameterAnalyzer(), @"
interface IDamageable
{
    void TakeDamage(int amount, string source);
}

abstract class Enemy
{
    public abstract void Swap(int first, int second);
}

class Goblin : Enemy, IDamageable
{
    public void TakeDamage(int {|TF1005:damage|}, string source) { }
    public override void Swap(int {|TF1005:second|}, int {|TF1005:first|}) { }
}

class Orc : IDamageable
{
    void IDamageable.TakeDamage(int {|TF1005:hit|}, string source) { }
}

partial class Partial
{
    partial void Changed(int value);
    partial void Changed(int {|TF1005:newValue|}) { }
}");
        }

        [Test]
        public void GenericBaseParameters_MayBeRenamed()
        {
            AnalyzerVerifier.Verify(new RenamedParameterAnalyzer(), @"
using System.Collections.Generic;
using Sisus.Init;
using UnityEngine;

interface IViewManager { }

class Menu : MonoBehaviour<IViewManager>
{
    protected override void Init(IViewManager viewManager) { }
}

class ByScore : IComparer<int>
{
    public int Compare(int left, int right) => left - right;
}");
        }

        [Test]
        public void TypeParameterKeptByTheImplementation_KeepsTheName()
        {
            AnalyzerVerifier.Verify(new RenamedParameterAnalyzer(), @"
using System.Collections.Generic;

interface IInit<T> { void Init(T first); }
interface IFill<T> { void Fill(List<T> items); void Copy(T[] items); }
interface IUse { void Use<T>(T item); }
abstract class Wrapper<T> { public abstract T InitTarget(T target); }

class Enemy { }

class ByHealth : IComparer<Enemy> { public int Compare(Enemy left, Enemy right) => 0; }
class ByAnything<T> : IComparer<T> { public int Compare(T {|TF1005:left|}, T {|TF1005:right|}) => 0; }
class Holder<T> : IInit<T> { public void Init(T {|TF1005:value|}) { } }
class ListHolder<U> : IInit<List<U>> { public void Init(List<U> values) { } }
class Filler : IFill<int> { public void Fill(List<int> values) { } public void Copy(int[] {|TF1005:values|}) { } }
class WrapperInitializer<TWrapper> : Wrapper<TWrapper> { public override TWrapper InitTarget(TWrapper {|TF1005:wrapper|}) => wrapper; }
class User : IUse { public void Use<T>(T {|TF1005:thing|}) { } }");
        }
    }
}