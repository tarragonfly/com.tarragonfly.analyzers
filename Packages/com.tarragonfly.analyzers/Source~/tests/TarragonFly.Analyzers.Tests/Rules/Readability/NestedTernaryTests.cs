using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class NestedTernaryTests
    {
        [Test]
        public void EveryNestedTernary_IsReported()
        {
            AnalyzerVerifier.Verify(new NestedTernaryAnalyzer(), @"
class HealthBar
{
    int Pick(int value) => value;

    string Color(int hp, bool low, bool flashing)
    {
        string a = hp > 50 ? ""green"" : {|TF1018:hp > 20 ? ""yellow"" : ""red""|};
        int b = ({|TF1018:low ? flashing : true|}) ? 1 : 2;
        int c = low ? Pick({|TF1018:flashing ? 1 : 2|}) : 3;
        int d = low ? {|TF1018:flashing ? {|TF1018:hp > 0 ? 1 : 2|} : 3|} : 4;
        string e = low ? $""{({|TF1018:flashing ? ""x"" : ""y""|})}"" : ""z"";
        return a + b + c + d + e;
    }
}");
        }

        [Test]
        public void LambdasAnonymousMethodsAndLocalFunctions_StartFresh()
        {
            AnalyzerVerifier.Verify(new NestedTernaryAnalyzer(), @"
using System;

class HealthBar
{
    int Pick(Func<int> value) => value();

    int Color(bool low, bool flashing)
    {
        int a = low ? Pick(() => flashing ? 1 : 2) : 3;
        int b = low ? Pick(delegate { return flashing ? 1 : 2; }) : 3;
        int c = low ? Local() : 0;
        return a + b + c;

        int Local() => flashing ? 1 : 2;
    }
}");
        }
    }
}