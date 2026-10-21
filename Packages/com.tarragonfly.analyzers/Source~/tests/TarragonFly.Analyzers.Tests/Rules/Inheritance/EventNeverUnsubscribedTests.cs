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
