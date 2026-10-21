using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Inheritance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Inheritance
{
    // TF2511 event subscribed but never unsubscribed.
    public class EventNeverUnsubscribedTests
    {
        [Test]
        public void SubscriptionWithoutAMatchingUnsubscription_IsReported()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using System;
using UnityEngine;
using UnityEngine.UI;

static class UserInput
{
    public static event Action OnPause;
    public static event Action OnResume;
    public static Action OnQuit;
}

class Door
{
    public event Action Opened;
    public event Action Closed;
    public event Action<int> Knocked;
}

class PauseMenu : MonoBehaviour
{
    [SerializeField] Button resumeButton;
    [SerializeField] Button quitButton;
    [SerializeField] Slider volume;
    Door door;
    Action onOpened;

    void OnEnable()
    {
        UserInput.OnPause += Show;
        {|TF2511:UserInput.OnResume += Hide|};
        {|TF2511:UserInput.OnQuit += Hide|};
        door.Opened += OnDoorOpened;
        {|TF2511:door.Closed += OnDoorClosed|};
        onOpened = () => { };
        door.Opened += onOpened;
        door.Closed += () => Hide();
        {|TF2511:door.Knocked += new Action<int>(OnKnocked)|};
        resumeButton.onClick.AddListener(Resume);
        {|TF2511:quitButton.onClick.AddListener(Quit)|};
        volume.onValueChanged.AddListener(SetVolume);
        volume.onValueChanged.AddListener(value => SetVolume(value));
    }

    void OnDisable()
    {
        UserInput.OnPause -= Show;
        door.Opened -= OnDoorOpened;
        door.Opened -= onOpened;
        door.Closed -= () => Hide();
        resumeButton.onClick.RemoveListener(Resume);
        volume.onValueChanged.RemoveAllListeners();
    }

    void Show() { }
    void Hide() { }
    void OnDoorOpened() { }
    void OnDoorClosed() { }
    void OnKnocked(int times) { }
    void Resume() { }
    void Quit() { }
    void SetVolume(float value) { }
}");
        }

        // The handler and the event are matched, not the object or the method the -= sits in.
        [Test]
        public void UnsubscriptionAnywhereInTheType_Counts()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using System;
using UnityEngine;

class Door
{
    public event Action Opened;
}

class Guard : MonoBehaviour
{
    Door front;
    Door back;

    void Awake()
    {
        front.Opened += Alert;
        back.Opened += Alert;
    }

    void Watch(Door door)
    {
        door.Opened += Alert;
    }

    void OnDestroy()
    {
        Action cleanup = () => front.Opened -= Alert;
        cleanup();
    }

    void Alert() { }
}

class Watcher : MonoBehaviour
{
    Door door;

    void Awake()
    {
        {|TF2511:door.Opened += Alert|};
    }

    void Alert() { }
}");
        }

        [Test]
        public void OwnEventsAndOncePerDomainSubscriptions_AreNotReported()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using System;
using UnityEngine;

static class UserInput
{
    public static event Action OnPause;
}

class Spawner : MonoBehaviour
{
    public event Action Spawned;
    public static event Action Reset;
    public Action onSpawned;

    void Awake()
    {
        Spawned += OnSpawned;
        this.Spawned += OnSpawned;
        onSpawned += OnSpawned;
        Reset += OnReset;
        Spawner.Reset += OnReset;
    }

    static Spawner()
    {
        UserInput.OnPause += OnPaused;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Boot()
    {
        UserInput.OnPause += OnPaused;
    }

    static void OnPaused() { }
    void OnSpawned() { }
    static void OnReset() { }
}

class Nest : Spawner
{
    void Start()
    {
        Reset += OnNestReset;
        base.Spawned += OnNestReset;
    }

    void OnNestReset() { }
}

static class Startup
{
    [RuntimeInitializeOnLoadMethod]
    static void Boot()
    {
        UserInput.OnPause += OnPaused;
    }

    static void OnPaused() { }
}");
        }

        // Components on the subscriber's GameObject or under it are destroyed along with it.
        [Test]
        public void EventsOfTheOwnGameObject_AreNotReported()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using UnityEngine;
using UnityEngine.UI;

class VolumePanel : MonoBehaviour
{
    [SerializeField] Button savedButton;
    Button closeButton;
    Slider volume;
    Toggle mute;
    ScrollRect presets;
    Transform options;
    Button parentButton;
    Button sharedButton;

    void Awake()
    {
        closeButton = GetComponent<Button>();
        options = transform.Find(""Options"");
        volume = options.GetComponentInChildren<Slider>();
        mute = gameObject.AddComponent<Toggle>();
        TryGetComponent(out presets);
        savedButton = GetComponent<Button>();
        parentButton = GetComponentInParent<Button>();
        sharedButton = GetComponent<Button>();
    }

    public void Share(Button button)
    {
        sharedButton = button;
    }

    void Start()
    {
        closeButton.onClick.AddListener(Close);
        volume.onValueChanged.AddListener(SetVolume);
        mute.onValueChanged.AddListener(SetMuted);
        GetComponent<Button>().onClick.AddListener(Close);
        if (TryGetComponent(out Slider own)) own.onValueChanged.AddListener(SetVolume);
        Toggle[] items = presets.content.GetComponentsInChildren<Toggle>();
        foreach (Toggle item in items) item.onValueChanged.AddListener(SetMuted);
        items[0].onValueChanged.AddListener(SetMuted);

        {|TF2511:savedButton.onClick.AddListener(Close)|};
        {|TF2511:parentButton.onClick.AddListener(Close)|};
        {|TF2511:sharedButton.onClick.AddListener(Close)|};
        Button found = FindAnyObjectByType<Button>();
        {|TF2511:found.onClick.AddListener(Close)|};
        Button picked = GetComponent<Button>();
        picked = found;
        {|TF2511:picked.onClick.AddListener(Close)|};
    }

    void Close() { }
    void SetVolume(float value) { }
    void SetMuted(bool muted) { }
}");
        }

        [Test]
        public void LoadCompletionsAndUiToolkitClicks_AreNotReported()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using System;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public struct AsyncOperationHandle<TObject>
    {
        public event Action<AsyncOperationHandle<TObject>> Completed;
    }
}

class Download
{
    public event Action Completed;
    public event Action clicked;
}

class OptionsMenu : MonoBehaviour
{
    Download download;

    void Open(Button close, AsyncOperationHandle<GameObject> prefab)
    {
        SceneManager.LoadSceneAsync(""Options"").completed += OnSceneLoaded;
        prefab.Completed += OnPrefabLoaded;
        close.clicked += Close;
        {|TF2511:download.Completed += Close|};
        {|TF2511:download.clicked += Close|};
    }

    void OnSceneLoaded(AsyncOperation operation) { }
    void OnPrefabLoaded(AsyncOperationHandle<GameObject> handle) { }
    void Close() { }
}");
        }

        [Test]
        public void EditorDelayCall_IsNotReported()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using UnityEditor;

class ScenarioWindow
{
    void Open()
    {
        EditorApplication.delayCall += Refresh;
        {|TF2511:EditorApplication.update += Tick|};
    }

    void Refresh() { }
    void Tick() { }
}", new SnippetAssembly { AssemblyName = "Game.Editor", EditorOnly = true });
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new EventNeverUnsubscribedAnalyzer(), @"
using System;

class Door
{
    public event Action Opened;
}

class DoorTests
{
    Door door;
    int opened;

    public void SetUp()
    {
        {|TF2511:door.Opened += Count|};
    }

    void Count() => opened++;
}", SnippetAssembly.Tests());
        }
    }
}
