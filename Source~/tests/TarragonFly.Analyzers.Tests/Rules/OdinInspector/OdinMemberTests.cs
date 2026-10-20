using NUnit.Framework;
using TarragonFly.Analyzers.Rules.OdinInspector;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.OdinInspector
{
    // TF6000 Odin attribute names a member that doesn't exist, TF6001 member of the wrong type.
    public class OdinMemberTests
    {
        private const string Code = @"
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

class EnemyBase : MonoBehaviour
{
    protected bool isFlying;
}

[InfoBox(""Boss setup"", {|TF6000:""showBossInfo""|})]
class Enemy : EnemyBase
{
    public bool isBoss;
    public float health;
    public string bossName;
    public List<string> names;

    [ShowIf({|TF6000:""isBos""|})] public int bossPhase;
    [ShowIf(""isBoss"")] public int phase;
    [ShowIf(""isFlying"")] public int altitude;
    [ShowIf(""IsDebug"")] public int debugOnly;
    [HideIf(""@health <= 0"")] public int armor;
    [EnableIf(""CanEdit"")] public int speed;
    [LabelText({|TF6000:""$displayNmae""|})] public int label;
    [LabelText(""$bossName"")] public int namedLabel;
    [LabelText(""$property.NiceName"")] public int propertyLabel;
    [LabelText(""Plain label"")] public int plainLabel;
    [OnValueChanged({|TF6000:""Refesh""|})] public int refreshed;
    [InlineButton(""Refresh"")] public int buttoned;
    [ShowIf(""bossName"", ""Dragon"")] public int dragonOnly;
    [InfoBox(""Low"", InfoMessageType.Warning, ""IsLow"")] public int lowBox;
    [InfoBox(""Hidden"", VisibleIf = {|TF6000:""IsHiden""|})] public int hiddenBox;

    [ShowIf({|TF6001:""health""|})] public int wrongCondition;
    [DisableIf({|TF6001:""bossName""|})] public int wrongDisable;
    [ValidateInput({|TF6001:""Refresh""|})] public int wrongValidator;
    [ValidateInput(""IsValid"")] public int validated;
    [ValueDropdown({|TF6001:""bossName""|})] public string wrongDropdown;
    [ValueDropdown(""names"")] public string dropdown;
    [ValueDropdown(""Options"")] public string fromMethod;
    [InfoBox(""Shape"", {|TF6001:""health""|})] public int wrongInfoBox;

    bool CanEdit() => true;
    bool IsLow => health < 10;
    static bool IsDebug => false;
    bool IsValid(int value) => value > 0;
    void Refresh() { }
    IEnumerable<string> Options() => names;
}";

        [Test]
        public void MissingMember_IsReported()
        {
            AnalyzerVerifier.Verify(new UnknownOdinMemberAnalyzer(), Markup.Without(Code, "TF6001"));
        }

        [Test]
        public void MemberOfTheWrongType_IsReported()
        {
            AnalyzerVerifier.Verify(new WrongOdinMemberTypeAnalyzer(), Markup.Without(Code, "TF6000"));
        }

        [Test]
        public void Message_GivesTheAttributeWhatItNeedsAndWhatTheMemberIs()
        {
            Assert.That(AnalyzerVerifier.Messages(new WrongOdinMemberTypeAnalyzer(), @"
using Sirenix.OdinInspector;

class Enemy
{
    public float health;
    [ShowIf(""health"")] public int wrongCondition;
}"),
                Is.EqualTo(new[] { "ShowIf needs a bool field or property, or a method returning bool, but 'health' is float" }));
        }
    }
}