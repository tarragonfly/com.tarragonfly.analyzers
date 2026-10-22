using NUnit.Framework;
using TarragonFly.Analyzers.Rules.OdinInspector;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.OdinInspector
{
    // TF6002 group nested in a group nothing declares, TF6003 one group with two group attribute types,
    // TF6004 member in groups that don't nest.
    public class OdinGroupPathTests
    {
        [Test]
        public void GroupPaths_AreChecked()
        {
            AnalyzerVerifier.Verify(new OdinGroupPathAnalyzer(), @"
using Sirenix.OdinInspector;
using UnityEngine;

class UnitBase : MonoBehaviour
{
    [BoxGroup(""Stats"")] public int level;
    [FoldoutGroup(""Loot"")] public int gold;
}

class Enemy : UnitBase
{
    [BoxGroup(""Stats/Combat"")] public int attack;
    [BoxGroup({|TF6002:""Combat/Ranged""|})] public int range;
    [BoxGroup(group: {|TF6002:""Missing/Deeper/Still""|})] public int deep;

    [TabGroup(""Tabs"", ""Moves"")] public int moveCount;
    [BoxGroup(""Tabs/Moves/Special"")] public int special;
    [BoxGroup({|TF6002:""Tabs/Gear/Potions""|})] public int potions;

    [HorizontalGroup(""Row"")] public int left;
    [FoldoutGroup({|TF6003:""Row""|})] public int right;
    [HorizontalGroup({|TF6003:""Loot""|})] public int silver;

    [HorizontalGroup(""Split""), VerticalGroup(""Split/Left"")] public int nested;
    [BoxGroup(""Stats/Combat""), BoxGroup(""Stats/Defense"")] public int {|TF6004:split|};
    [HorizontalGroup(""Row""), BoxGroup(""RowB"")] public int {|TF6004:sideBySide|};
    [TabGroup(""Tabs"", ""Moves""), TabGroup(""Tabs"", ""Items"")] public int {|TF6004:twoTabs|};

    [ButtonGroup] void Attack() { }
    [ButtonGroup] void Defend() { }
}");
        }

        [Test]
        public void TypeWithAGroupItCantRead_SkipsUnknownParents()
        {
            AnalyzerVerifier.Verify(new OdinGroupPathAnalyzer(), @"
using Sirenix.OdinInspector;
using UnityEngine;

class Layout : MonoBehaviour
{
    [ColumnGroup(""Columns"", ""Left"", 0.5f)] public int left;
    [BoxGroup(""Columns/Left/Box"")] public int boxed;
}");
        }

        [Test]
        public void Messages_NameThePathsAndTheMember()
        {
            Assert.That(AnalyzerVerifier.Messages(new OdinGroupPathAnalyzer(), @"
using Sirenix.OdinInspector;
using UnityEngine;

class Enemy : MonoBehaviour
{
    [BoxGroup(""Combat/Ranged"")] public int range;
    [HorizontalGroup(""Row"")] public int left;
    [FoldoutGroup(""Row"")] public int right;
    [BoxGroup(""A""), BoxGroup(""B"")] public int split;
}"),
                Is.EquivalentTo(new[]
                {
                    "'Combat/Ranged' sits inside 'Combat', but nothing on 'Enemy' declares a 'Combat' group",
                    "'Row' is a FoldoutGroup here but a HorizontalGroup on 'left'; use one group attribute type per group",
                    "'split' is in both 'A' and 'B'; one of the groups has to sit inside the other"
                }));
        }
    }
}