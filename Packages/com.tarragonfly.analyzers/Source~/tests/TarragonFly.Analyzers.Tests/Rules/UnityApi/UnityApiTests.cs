using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.UnityApi;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.UnityApi
{
    // TF4100 new lambda unregistered, TF4101 serialized field Unity can't save, TF4102 scene search
    // filling in an Inspector reference, TF4103 to TF4105 OnValidate reaching other objects.
    public class UnityApiTests
    {
        [Test]
        public void OnValidateFindingOrAssigningAReference_IsReported()
        {
            AnalyzerVerifier.Verify(new OnValidateReachesOtherObjectsAnalyzer(), @"
using System.Collections.Generic;
using UnityEngine;

class Hud : MonoBehaviour { }
class Skeleton : MonoBehaviour { }
class Clip : Object { }

[System.Serializable]
class Channel
{
    public float volume;
    public Clip clip;
    public List<Clip> clips;
}

class Player : MonoBehaviour
{
    [SerializeField] Skeleton skeleton;
    [SerializeField] Hud hud;
    [SerializeField] Clip clip;
    [SerializeField] Channel music;
    [SerializeField] int poolSize;
    [SerializeField] string id;
    [SerializeField] Hud[] huds;
    [SerializeField] List<Hud> hudList;
    [SerializeField] List<int> counts;
    Transform cachedRoot;
    Skeleton cachedSkeleton;
    public Hud Owner { get; set; }
    Skeleton CachedSkeleton => {|TF4103:cachedSkeleton ??= GetComponent<Skeleton>()|};
    Transform Target => GetComponent<Transform>();

    int PoolSize
    {
        get => poolSize;
        set
        {
            poolSize = value;
            {|TF4103:skeleton = GetComponent<Skeleton>()|};
        }
    }

    int Size
    {
        get => poolSize;
        set => skeleton = GetComponent<Skeleton>();
    }

    int Spawned
    {
        get => 0;
        set => {|TF4103:hud = GetComponent<Hud>()|};
    }

    int Wave
    {
        get => 0;
        set => {|TF4103:clip = Resources.Load<Clip>(""Clips/Wave"")|};
    }

    int Level
    {
        get { skeleton = null; return 0; }
        set => {|TF4103:cachedRoot = transform.parent|};
    }

    void OnValidate()
    {
        {|TF4103:skeleton = GetComponent<Skeleton>()|};
        if (hud == null) {|TF4103:hud = FindFirstObjectByType<Hud>()|};
        {|TF4103:hud ??= GameObject.Find(""Hud"").GetComponent<Hud>()|};
        {|TF4103:clip = Resources.Load<Clip>(""Clips/Jump"")|};
        {|TF4103:this.hud = null|};
        {|TF4103:cachedRoot = transform.parent|};
        {|TF4103:Owner = hud|};
        {|TF4103:music.clip = clip|};
        {|TF4103:GetComponentInChildren<Hud>|}();
        {|TF4103:TryGetComponent|}(out Skeleton found);
        Transform spawn = {|TF4103:transform.Find|}(""Spawn"");
        Skeleton other = {|TF4103:FindAnyObjectByType<Skeleton>|}();
        SanitizePool();
        LookUpSkeleton();
        {|TF4103:huds[0] = hud|};
        {|TF4103:hudList[0] = null|};
        {|TF4103:hudList.Add|}(hud);
        {|TF4103:hudList.Add|}(GetComponent<Hud>());
        {|TF4103:hudList.RemoveAll|}(entry => entry == null);
        {|TF4103:music.clips.Clear|}();
        hudList?{|TF4103:.RemoveAll|}(entry => entry == null);
        music.clips?{|TF4103:.Clear|}();
        {|TF4104:name = id|};
        Debug.Log(nameof(Target), this);
        bool hasSkeleton = CachedSkeleton != null;
        PoolSize = 2;
        Spawned += 1;
        Wave++;
        (Level, poolSize) = (1, 2);
        {|TF4103:huds = null|};
        {|TF4103:hudList = new List<Hud>()|};
        {|TF4103:huds = GetComponents<Hud>()|};

        poolSize = (int)Mathf.Max(1, poolSize);
        counts = new List<int>();
        id = id.Trim();
        music.volume = Mathf.Clamp(music.volume, 0f, 1f);
        enabled = poolSize > 0;
        if (skeleton == null) Debug.LogError(""[Player] Skeleton is not assigned."", this);
        Hud read = hud;
        int size = Size;
        counts.Add(1);
        bool listed = hudList.Contains(hud);
        List<Hud> scratch = new List<Hud>();
        scratch.Add(hud);
        scratch[0] = hud;
    }

    void SanitizePool()
    {
        if (poolSize < 1) poolSize = 1;
    }

    void LookUpSkeleton()
    {
        if (skeleton == null) {|TF4103:skeleton = GetComponent<Skeleton>()|};
    }

    void Awake()
    {
        skeleton = GetComponent<Skeleton>();
        LookUpSkeleton();
    }

    void Reset()
    {
        skeleton = GetComponent<Skeleton>();
    }
}

class Settings : ScriptableObject
{
    [SerializeField] Hud hud;
    [SerializeField] string id;

    void OnValidate()
    {
        id = id.ToUpperInvariant();
        {|TF4103:hud = FindFirstObjectByType<Hud>()|};
        {|TF4105:name = id|};
    }
}

class NotAUnityObject
{
    Hud hud;

    void OnValidate()
    {
        hud = Object.FindFirstObjectByType<Hud>();
    }
}");
        }

        [Test]
        public void OnValidateChangingAnotherObject_IsReported()
        {
            AnalyzerVerifier.Verify(new OnValidateReachesOtherObjectsAnalyzer(), @"
using UnityEngine;

class Label : MonoBehaviour
{
    public string text;
    public void SetText(string value) { }
    public bool Has(string value) => false;
}

[System.Serializable]
class Bucket
{
    public System.Collections.Generic.List<GameObject> prefabs;
}

class Catalog : ScriptableObject
{
    public System.Collections.Generic.List<GameObject> prefabs;
    public int[] counts;
    public Bucket data;
    public void Clear() { }
}

class VersionSetter : MonoBehaviour
{
    [SerializeField] Label versionText;
    [SerializeField] Catalog catalog;

    void OnValidate()
    {
        SetVersionText();
        {|TF4104:versionText.SetText|}(""v1"");
        versionText?{|TF4104:.SetText|}(""v2"");
        {|TF4104:transform.position = new Vector3()|};
        {|TF4104:gameObject.SetActive|}(false);
        {|TF4104:transform.parent.localScale = new Vector3()|};
        {|TF4104:catalog.prefabs[0] = null|};
        {|TF4104:catalog.counts[0] = 1|};
        {|TF4104:catalog.data.prefabs.Add|}(null);
        {|TF4104:catalog.prefabs.Clear|}();
        {|TF4104:catalog.Clear|}();
        catalog?{|TF4104:.prefabs.Clear|}();
        catalog?{|TF4104:.data.prefabs.Add|}(null);
        bool listed = catalog.prefabs.Contains(null);

        bool has = versionText.Has(""v"");
        string read = versionText.text;
        Debug.Log(versionText.text, this);
        this.enabled = false;
    }

    void SetVersionText()
    {
        {|TF4104:versionText.text = ""v1""|};
    }

    void Start()
    {
        SetVersionText();
    }
}");
        }

        [Test]
        public void OnValidateCreatingDestroyingOrSavingObjects_IsReported()
        {
            AnalyzerVerifier.Verify(new OnValidateReachesOtherObjectsAnalyzer(), @"
using UnityEngine;
using UnityEditor;

class Hud : MonoBehaviour { }
class Preset : ScriptableObject { }

class Spawner : MonoBehaviour
{
    [SerializeField] Hud hudPrefab;
    [SerializeField] Material material;
    Hud spawned;

    void OnValidate()
    {
        {|TF4105:Instantiate|}(hudPrefab);
        {|TF4105:PrefabUtility.InstantiatePrefab|}(hudPrefab);
        {|TF4105:Undo.AddComponent<Hud>|}(gameObject);
        Material preview = {|TF4105:new Material(material)|};
        Mesh shape = {|TF4105:new Mesh()|};
        {|TF4103:spawned = {|TF4105:Instantiate|}(hudPrefab)|};
        {|TF4105:Destroy|}(spawned);
        {|TF4105:DestroyImmediate|}(spawned);
        {|TF4105:gameObject.AddComponent<Hud>|}();
        Preset made = {|TF4105:ScriptableObject.CreateInstance<Preset>|}();
        GameObject holder = {|TF4105:new GameObject(""Holder"")|};
        GameObject shorthand = {|TF4105:new(""Holder"")|};
        {|TF4105:EditorUtility.SetDirty|}(this);
        {|TF4105:AssetDatabase.SaveAssets|}();
        {|TF4105:AssetDatabase.DeleteAsset|}(""Assets/Old.asset"");
        {|TF4105:Undo.RecordObject|}(this, ""Validate"");
        {|TF4105:Undo.DestroyObjectImmediate|}(spawned);
        EditorApplication.delayCall += Save;
    }

    void Save()
    {
        {|TF4105:EditorUtility.SetDirty|}(this);
    }
}");
        }

        [Test]
        public void OnValidateMessage_NamesTheHelperItReachesThrough()
        {
            Assert.That(AnalyzerVerifier.Messages(new OnValidateReachesOtherObjectsAnalyzer(), @"
using UnityEngine;

class Label : MonoBehaviour
{
    public string text;
}

class Catalog : ScriptableObject
{
    public System.Collections.Generic.List<GameObject> prefabs;
}

class VersionSetter : MonoBehaviour
{
    [SerializeField] Label versionText;
    [SerializeField] Hud hud;
    [SerializeField] System.Collections.Generic.List<Hud> huds;
    [SerializeField] Catalog catalog;
    Hud CachedHud => hud ??= GetComponent<Hud>();

    void OnValidate()
    {
        SetVersionText();
        hud = GetComponent<Hud>();
        huds.Add(GetComponent<Hud>());
        name = ""Version"";
        Hud first = CachedHud;
        huds = null;
        catalog?.prefabs.Clear();
    }

    void SetVersionText()
    {
        versionText.text = ""v1"";
    }
}

class Preset : ScriptableObject
{
    void OnValidate()
    {
        name = ""Preset"";
        Material preview = new Material(null);
    }
}

class Hud : MonoBehaviour { }"),
                Is.EqualTo(new[]
                {
                    "OnValidate sets 'hud' from 'GetComponent' through 'CachedHud'; assign references in the Inspector or look them up in Awake",
                    "OnValidate sets 'hud' from 'GetComponent'; assign references in the Inspector or look them up in Awake",
                    "OnValidate changes the reference list 'huds' with 'Add' from 'GetComponent'; assign references in the Inspector or look them up in Awake",
                    "OnValidate renames the GameObject with 'name'; change other objects from an editor command or at runtime",
                    "OnValidate assigns the reference list 'huds'; assign references in the Inspector or look them up in Awake",
                    "OnValidate changes 'catalog?.prefabs' with 'Clear'; change other objects from an editor command or at runtime",
                    "OnValidate changes 'versionText.text' through 'SetVersionText'; change other objects from an editor command or at runtime",
                    "OnValidate renames the asset with 'name'; do that from an editor command",
                    "OnValidate creates a new Material; do that from an editor command"
                }));
        }

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
        public void Message_GivesTheFieldAndWhyUnityCantSaveIt()
        {
            Assert.That(AnalyzerVerifier.Messages(new UnsaveableSerializeFieldAnalyzer(), @"
using UnityEngine;

interface IWeapon { }

class Player : MonoBehaviour
{
    [SerializeField] IWeapon weapon;
}"),
                Is.EqualTo(new[] { "Unity can't save 'weapon' (an interface without [SerializeReference]), so its value never reaches the Inspector or the saved scene" }));
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