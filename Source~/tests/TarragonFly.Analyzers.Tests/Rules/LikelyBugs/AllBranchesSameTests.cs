using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    public class AllBranchesSameTests
    {
        [Test]
        public void EveryBranchTheSame_IsReported()
        {
            AnalyzerVerifier.Verify(new AllBranchesSameAnalyzer(), @"
class Weapon
{
    int damage;

    int Hit(int kind, bool critical, int baseDamage)
    {
        {|TF2059:if|} (critical) damage++;
        else damage++;

        {|TF2059:if|} (kind == 1) { damage++; }
        else if (kind == 2) { damage++; }
        else { damage++; }

        {|TF2059:switch|} (kind)
        {
            case 1:
                damage++;
                break;
            default:
                damage++;
                break;
        }

        int a = {|TF2059:critical ?|} baseDamage : baseDamage;
        int b = kind {|TF2059:switch|} { 1 => 5, _ => 5 };
        return a + b;
    }
}");
        }

        [Test]
        public void MissingElseOrDefault_OrADifferentBranch_IsFine()
        {
            AnalyzerVerifier.Verify(new AllBranchesSameAnalyzer(), @"
class Weapon
{
    int damage;

    int Hit(int kind, bool critical, int baseDamage)
    {
        if (kind == 1) { damage++; }
        else if (kind == 2) { damage++; }

        switch (kind)
        {
            case 1:
                damage++;
                break;
            case 2:
                damage++;
                break;
        }

        if (critical) damage += 2;
        else damage++;

        int a = critical ? baseDamage * 2 : baseDamage;
        int b = kind switch { 1 => 5, 2 => 5, _ => 6 };
        return a + b;
    }
}");
        }
    }
}