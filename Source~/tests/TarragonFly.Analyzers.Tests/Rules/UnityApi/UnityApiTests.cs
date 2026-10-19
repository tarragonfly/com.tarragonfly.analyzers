using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.UnityApi;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.UnityApi
{
    // TF4100 new lambda unregistered, TF4101 serialized field Unity can't save, TF4102 scene search
    // filling in an Inspector reference.
    public class UnityApiTests
    {
        [Test]
        public void NewLambdaUnregistered_IsReported()
        {
            AnalyzerVerifier.Verify(new UnregisterNewLambdaAnalyzer(), @"
using System;
using UnityEngine.UIElements;

class PauseView
{
    Action onClose;
    EventCallback<ClickEvent> clickHandler;

    void Bind(Button play, Toggle music)
    {
        play.RegisterCallback<ClickEvent>(evt => Play());
        play.UnregisterCallback<ClickEvent>({|TF4100:evt =>|} Play());
        play.UnregisterCallback<ClickEvent>({|TF4100:delegate (ClickEvent evt)|} { Play(); });
        music.UnregisterValueChangedCallback({|TF4100:(ChangeEvent<bool> evt) =>|} Play());
        onClose {|TF4100:-= () => Play()|};

        play.UnregisterCallback<ClickEvent>(OnClick);
        play.UnregisterCallback(clickHandler);
        onClose -= Play;
        play.clicked -= () => Play();
    }

    void Play() { }
    void OnClick(ClickEvent evt) { }
}");
        }

        [Test]
        public void SerializedFieldOfATypeUnityCantSave_IsReported()
        {
            AnalyzerVerifier.Verify(new UnsaveableSerializeFieldAnalyzer(), @"
using System;
using System.Collections.Generic;
using UnityEngine;

interface IWeapon { }
abstract class Ability { }
abstract class AbilityAsset : ScriptableObject { }

class Player : MonoBehaviour
{
    [SerializeField] Dictionary<string, int> {|TF4101:scores|};
    [SerializeField] HashSet<int> {|TF4101:unlocked|};
    [SerializeField] IWeapon {|TF4101:weapon|};
    [SerializeField] Ability {|TF4101:ability|};
    [SerializeField] object {|TF4101:anything|};
    [SerializeField] int[,] {|TF4101:grid|};
    [SerializeField] List<int[]> {|TF4101:waves|};
    [SerializeField] Action {|TF4101:onDeath|};
    [SerializeField] int? {|TF4101:bestTime|};
    [SerializeField] List<Dictionary<int, int>> {|TF4101:levels|};
    [field: SerializeField] Queue<int> {|TF4101:Moves|} { get; set; }

    [SerializeField] List<int> fine;
    [SerializeField] int[] alsoFine;
    [SerializeField] AbilityAsset unityObject;
    [SerializeField, SerializeReference] IWeapon referenced;
    [SerializeField, SerializeReference] Ability referencedAbility;
    Dictionary<string, int> notSerialized;
    public Dictionary<string, int> publicButNotMarked;
}

class Boss : Sirenix.OdinInspector.SerializedMonoBehaviour
{
    [SerializeField] Dictionary<string, int> savedByOdin;
}");
        }

        [Test]
        public void Unity6000_6AndNewer_ChecksSerializationItself()
        {
            const string Code = @"
using System.Collections.Generic;
using UnityEngine;

class Player : MonoBehaviour
{
    [SerializeField] Dictionary<string, int> scores;
}";
            CSharpParseOptions options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9).WithPreprocessorSymbols("UNITY_6000_6_OR_NEWER");
            Assert.That(AnalyzerVerifier.Run(new UnsaveableSerializeFieldAnalyzer(), Code, new SnippetAssembly { ParseOptions = options }), Is.Empty);
        }

        [Test]
        public void SceneSearchFillingAMissingReference_IsReported()
        {
            AnalyzerVerifier.Verify(new SceneSearchRepairsReferenceAnalyzer(), @"
using UnityEngine;

class Hud : MonoBehaviour { }

class Player : MonoBehaviour
{
    [SerializeField] Hud hud;
    public GameObject spawnPoint;
    Hud cachedHud;

    void Awake()
    {
        if (hud == null) hud = {|TF4102:FindFirstObjectByType<Hud>|}();
        if (!spawnPoint)
        {
            spawnPoint = {|TF4102:GameObject.Find|}(""Spawn"");
        }
        if (hud is null) this.hud = {|TF4102:Object.FindAnyObjectByType<Hud>|}();
        hud ??= {|TF4102:FindObjectOfType<Hud>|}();
        hud = hud ?? {|TF4102:FindObjectOfType<Hud>|}();

        if (cachedHud == null) cachedHud = FindFirstObjectByType<Hud>();
        if (hud == null) Debug.LogError(""[Player] Hud is not assigned."", this);
        Hud other = FindFirstObjectByType<Hud>();
    }

    void Reset()
    {
        if (hud == null) hud = FindFirstObjectByType<Hud>();
    }

    void OnValidate()
    {
        hud ??= FindFirstObjectByType<Hud>();
    }
}

class Settings : ScriptableObject
{
    [SerializeField] Hud hud;

    void OnEnable()
    {
        if (hud == null) hud = FindFirstObjectByType<Hud>();
    }
}");
        }
    }
}