using NUnit.Framework;
using TarragonFly.Analyzers.Rules.DeadCode;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.DeadCode
{
    // TF1501 unused locals, TF1503 unused type parameters.
    public class UnusedDeclarationTests
    {
        [Test]
        public void LocalsNeverUsed_AreReported()
        {
            AnalyzerVerifier.Verify(new UnusedLocalAnalyzer(), @"
using System;

class Locals
{
    static int Compute() => 1;

    void Run(object o)
    {
        int {|TF1501:computed|} = Compute();
        const int {|TF1501:Limit|} = 4;
        if (o is string {|TF1501:text|}) { }
        bool parsed = int.TryParse(""1"", out int {|TF1501:value|});
        var (first, {|TF1501:second|}) = (1, 2);
        object {|TF1501:fromCast|} = o as string;
        Console.WriteLine(parsed + """" + first);
    }
}");
        }

        // The compiler warns about the first three itself (CS0168, CS0219).
        [Test]
        public void UsedLocalsAndCompilerWarnedLocals_AreNotReported()
        {
            AnalyzerVerifier.Verify(new UnusedLocalAnalyzer(), @"
using System;
using System.IO;

class Locals
{
    static int Compute() => 1;

    void Run(int[] items)
    {
        int declaredOnly;
        int constant = 5;
        try { } catch (Exception e) { }
        int used = Compute();
        int assignedLater;
        assignedLater = Compute();
        foreach (int item in items) { }
        using (StreamReader reader = new StreamReader(""x"")) { }
        using StreamReader declaration = new StreamReader(""y"");
        Func<int, int> f = x => used;
        int _ = Compute();
        Console.WriteLine(f(1));
    }
}");
        }

        [Test]
        public void TypeParametersNothingUses_AreReported()
        {
            AnalyzerVerifier.Verify(new UnusedTypeParameterAnalyzer(), @"
using System;
using System.Collections.Generic;

interface IMarker<{|TF1503:T|}> { }

class Pool<{|TF1503:T|}>
{
    public void Clear<{|TF1503:TItem|}>() { }
    public void Constrained<{|TF1503:TItem|}>() where TItem : class { }
    public void Used<TItem>(TItem item) { }
    public TResult Make<TResult>() => default;
    public void InBody<TItem>() { Console.WriteLine(typeof(TItem)); }
    public void InArgument<TItem>(List<TItem> items) { }
}

class Box<T>
{
    T value;
}");
        }

        [Test]
        public void FixedSignaturesAndDelegates_AreSkipped()
        {
            AnalyzerVerifier.Verify(new UnusedTypeParameterAnalyzer(), @"
interface IHandler
{
    void Handle<TValue>();
}

abstract class Base
{
    public abstract void Make<T>();
    public virtual void Shape<T>() { }
}

class Derived : Base, IHandler
{
    public void Handle<TValue>() { }
    public override void Make<T>() { }
}

delegate void Callback<T>();");
        }
    }
}