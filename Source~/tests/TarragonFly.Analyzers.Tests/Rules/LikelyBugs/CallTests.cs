using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2008 repeated conditions, TF2021 swapped arguments, TF2044 params overloads, TF2049 hidden defaults.
    public class CallTests
    {
        [Test]
        public void RepeatedElseIfCondition_IsReported()
        {
            AnalyzerVerifier.Verify(new RepeatedConditionAnalyzer(), @"
class Enemy
{
    static int Next() => 1;

    void Think(int state, bool alert)
    {
        if (state == 1) { }
        else if (state == 2) { }
        else if ({|TF2008:state == 1|}) { }

        if (Next() == 1) { }
        else if ({|TF2008:Next() == 1|}) { }

        if (alert) { }
        if (alert) { }
        int x = state == 1 ? 1 : state == 1 ? 2 : 3;
    }
}");
        }

        [Test]
        public void SwappedArguments_AreReportedOnTheName()
        {
            AnalyzerVerifier.Verify(new SwappedArgumentsAnalyzer(), @"
using System;

class Size
{
    public Size(int width, int height) { }
}

class Mover
{
    void Move(int x, int y) { }
    void Hit(string source, string target, int damage) { }

    void Run(int x, int y, string source, string target, int damage, int width, int height)
    {
        {|TF2021:Move|}(y, x);
        {|TF2021:Hit|}(target, source, damage);
        new Mover().{|TF2021:Move|}(y, x);
        Size size = new {|TF2021:Size|}(height, width);

        Move(x, y);
        Move(y: y, x: x);
        Move(y, 1);
        Hit(target, target, damage);
        Math.Max(y, x);
        Action<int, int> move = Move;
        move(y, x);
    }
}");
        }

        [Test]
        public void CallPackedIntoParamsDespiteAnExactOverload_IsReported()
        {
            AnalyzerVerifier.Verify(new AccidentalParamsCallAnalyzer(), @"
class Logger
{
    void Format(string a, params object[] b) { }
    void Format(object a, object b, object c) { }
    void Show(params int[] values) { }
    void Show(long a, long b) { }

    void Run()
    {
        {|TF2044:Format("""", null, null)|};
        {|TF2044:Show(1, 2)|};
        Format(1, 2, 3);
        Format("""", 1);
        Show(1, 2, 3);
    }
}");
        }

        [Test]
        public void DefaultHiddenByAnotherOverload_IsReportedOnTheParameter()
        {
            AnalyzerVerifier.Verify(new UnreachableDefaultParameterAnalyzer(), @"
class Audio
{
    public void Play(string clip) { }
    public void Play(string clip, {|TF2049:float volume = 1f|}) { }

    public void Stop() { }
    public static void Stop({|TF2049:int fade = 0|}) { }

    public void Seek(int a, int b = 0, int c = 0) { }
    public void Seek(int a, int b) { }

    public void Pan(int a, float b = 0) { }
    public void Pan(int a, int b) { }
}");
        }
    }
}