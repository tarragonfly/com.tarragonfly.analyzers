using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2027 automatic values, TF2028 zero not named None, TF2063 values that aren't flags.
    public class FlagsEnumTests
    {
        [Test]
        public void AutomaticValuesPastOne_AreReportedOnTheEnum()
        {
            AnalyzerVerifier.Verify(new FlagsWithoutValuesAnalyzer(), @"
using System;

[Flags]
enum {|TF2027:Abilities|} { None, Jump, Run, Swim }

[Flags]
enum {|TF2027:Mixed|} { None = 0, Jump = 1, Run, Swim = 4 }

[Flags]
enum Pair { None, Jump }

[Flags]
enum Explicit { None = 0, Jump = 1, Run = 2 }

enum Plain { A, B, C, D }");
        }

        [Test]
        public void ZeroNotNamedNone_IsReportedOnTheMember()
        {
            AnalyzerVerifier.Verify(new FlagsZeroNotNoneAnalyzer(), @"
using System;

[Flags]
enum Abilities { {|TF2028:Nothing = 0|}, Jump = 1 }

[Flags]
enum Implicit { {|TF2028:Zero|}, Jump }

[Flags]
enum ByExpression { {|TF2028:Empty = 1 - 1|}, Jump = 1 }

[Flags]
enum Good { None = 0, Default = 0, Jump = 1 }

[Flags]
enum NoZero { Jump = 1, Run = 2 }

enum Plain { Nothing = 0, Jump = 1 }");
        }

        [Test]
        public void ValuesOutsideTheFlags_AreReportedOnTheEnum()
        {
            AnalyzerVerifier.Verify(new FlagsWithNonFlagValuesAnalyzer(), @"
using System;

[Flags]
enum {|TF2063:Abilities|} { None = 0, Jump = 1, Run = 2, Both = 3, Odd = 5 }

[Flags]
enum Combined { None = 0, Jump = 1, Run = 2, Swim = 4, All = Jump | Run | Swim }

[Flags]
enum Sequential { A, B, C, D }

[Flags]
enum Everything { None = 0, All = -1 }

enum Plain { A = 3, B = 5 }");
        }
    }
}