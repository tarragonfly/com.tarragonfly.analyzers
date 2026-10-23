using NUnit.Framework;
using TarragonFly.Analyzers.Rules.DependencyInjection;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.DependencyInjection
{
    // TF6500-TF6506: Init(args) service registration, lookup and Init(...) rules.
    public class InitArgsRulesTests
    {
        [Test]
        public void BareServiceOnAConfiguredComponent_IsReported()
        {
            AnalyzerVerifier.Verify(new ServiceLosesInspectorValuesAnalyzer(), @"
using Sisus.Init;
using UnityEngine;

interface IAudioService { }
interface IClock { }

[{|TF6500:Service(typeof(IAudioService))|}]
class AudioManager : MonoBehaviour, IAudioService
{
    [SerializeField] float volume;
}

class ConfiguredBase : MonoBehaviour
{
    public float speed;
}

[{|TF6500:Service|}]
class Mover : ConfiguredBase { }

[{|TF6500:Service(typeof(IClock))|}]
class ClockSettings : ScriptableObject, IClock
{
    [SerializeField] int hours;
}

[Service(typeof(IAudioService), LoadScene = ""00_Bootstrap"", DontDestroyOnLoad = false)]
class SceneAudio : MonoBehaviour, IAudioService
{
    [SerializeField] float volume;
}

[Service(typeof(IAudioService), FindFromScene = true)]
class FoundAudio : MonoBehaviour, IAudioService
{
    [SerializeField] float volume;
}

[Service(typeof(IClock), ResourcePath = ""Clock"")]
class LoadedClock : ScriptableObject, IClock
{
    [SerializeField] int hours;
}

[Service(typeof(IClock))]
class EmptyClock : MonoBehaviour, IClock
{
    float notSaved;
}

[Service(typeof(IClock))]
class PlainClock : IClock
{
    public int hours;
}");
        }

        [Test]
        public void SceneServiceArguments_AreChecked()
        {
            const string Code = @"
using Sisus.Init;
using UnityEngine;

interface IAudioService { }

[Service(typeof(IAudioService), {|TF6501:LoadScene = ""00_Bootstrap""|})]
class Audio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), {|TF6501:LoadScene = ""00_Bootstrap""|}, DontDestroyOnLoad = true)]
class MovedAudio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), LoadScene = ""00_Bootstrap"", DontDestroyOnLoad = false, {|TF6502:LazyInit = true|})]
class LazyAudio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), FindFromScene = true, {|TF6502:LoadAsync = true|})]
class AsyncAudio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), LoadScene = ""00_Bootstrap"", DontDestroyOnLoad = false)]
class BootstrapAudio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), FindFromScene = true)]
class SceneAudio : MonoBehaviour, IAudioService { }

[Service(typeof(IAudioService), ResourcePath = ""Audio"", LazyInit = true)]
class ResourceAudio : ScriptableObject, IAudioService { }";
            AnalyzerVerifier.Verify(new SceneServiceDontDestroyOnLoadAnalyzer(), Markup.Without(Code, "TF6502"));
            AnalyzerVerifier.Verify(new LazySceneServiceAnalyzer(), Markup.Without(Code, "TF6501"));
        }

        [Test]
        public void ServiceLookupsInComponents_AreReported()
        {
            const string Code = @"
using Sisus.Init;
using UnityEngine;

interface IAudioService { void Play(); }
class Clock { }

class Door : MonoBehaviour
{
    [SerializeField] Any<IAudioService> audio;
    [SerializeField] Any<Clock> clock;

    void Open()
    {
        Service<IAudioService>.{|TF6503:Instance|}.Play();
        audio.{|TF6504:Value|}.Play();
        Clock converted = {|TF6504:clock|};
        Any<Clock> wrapped = new Clock();
        clock = converted;
        audio.GetValue(this).Play();
        if (Service.TryGetFor(this, out IAudioService optional)) optional.Play();
    }

    class Hinge
    {
        void Swing(Any<IAudioService> sound) => Service<IAudioService>.{|TF6503:Instance|}.Play();
    }
}

class PlaySfxCommand
{
    Any<IAudioService> audio;

    void Execute()
    {
        audio.Value.Play();
        Service<IAudioService>.Instance.Play();
    }
}";
            AnalyzerVerifier.Verify(new ServiceInstanceInMonoBehaviourAnalyzer(), Markup.Without(Code, "TF6504"));
            AnalyzerVerifier.Verify(new AnyValueInComponentAnalyzer(), Markup.Without(Code, "TF6503"));
        }

        [Test]
        public void WorkStartedFromInit_IsReported()
        {
            AnalyzerVerifier.Verify(new WorkStartedFromInitAnalyzer(), @"
using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Sisus.Init;
using UnityEngine.SceneManagement;

interface IAudioService { }

class Intro : MonoBehaviour<IAudioService>
{
    IAudioService audio;

    protected override void Init(IAudioService audio)
    {
        this.audio = audio;
        {|TF6505:PlayIntroAsync|}().Forget();
        {|TF6505:StartCoroutine|}(Fade());
        {|TF6505:SceneManager.LoadSceneAsync|}(""Menu"");
        {|TF6505:UniTask.Void|}(async () => await UniTask.Yield());
        {|TF6505:ShakeCamera|}();
        System.Action later = () => PlayIntroAsync().Forget();
    }

    UniTask PlayIntroAsync() => UniTask.CompletedTask;
    async UniTaskVoid ShakeCamera() { await UniTask.Yield(); }
    IEnumerator Fade() { yield break; }

    void Init(int notInitArgs)
    {
        PlayIntroAsync().Forget();
    }
}

class AsyncIntro : MonoBehaviour<IAudioService>
{
    protected override {|TF6505:async|} void Init(IAudioService audio)
    {
        await Task.Yield();
    }
}");
        }

        [Test]
        public void ManualRegistration_IsReportedOutsideAllowedTypes()
        {
            AnalyzerVerifier.Verify(new ManualServiceRegistrationAnalyzer(), @"
using Sisus.Init;
using UnityEngine;

namespace Game.UI
{
    interface IViews { }

    class ViewManager : MonoBehaviour, IViews
    {
        void Awake() => Service.Set<IViews>(this);

        class Reloader
        {
            void Reload(ViewManager views) => Service.SetInstance<IViews>(views);
        }
    }

    class Inventory : MonoBehaviour, IViews
    {
        void Awake()
        {
            Service.{|TF6506:Set|}<IViews>(this);
            Service.{|TF6506:SetInstance|}<IViews>(this);
            Service.Get<IViews>();
        }
    }
}", SnippetAssembly.WithSettings("allowed_service_set_types = Game.UI.ViewManager"));
        }
    }
}