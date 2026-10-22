using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    // TF5100 task used twice, TF5101 token not last, TF5102 Async suffix, TF5103 MonoBehaviour await
    // without a token, TF5104 async lambda as a void delegate.
    public class UniTaskRulesTests
    {
        [Test]
        public void TaskUsedTwice_IsReportedOnTheSecondUse()
        {
            AnalyzerVerifier.Verify(new TaskAwaitedTwiceAnalyzer(), @"
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

class Fader
{
    UniTask FadeAsync() => UniTask.CompletedTask;
    UniTask<int> CountAsync() => default;
    ValueTask LoadAsync() => default;

    async UniTask Twice()
    {
        UniTask fade = FadeAsync();
        await fade;
        await {|TF5100:fade|};
    }

    async UniTask ForgetAfterAwait()
    {
        UniTask fade = FadeAsync();
        await fade;
        {|TF5100:fade|}.Forget();
    }

    async UniTask WhenAllAfterAwait(UniTask other)
    {
        await other;
        await UniTask.WhenAll({|TF5100:other|});
    }

    async UniTask InALoop()
    {
        UniTask<int> count = CountAsync();
        for (int i = 0; i < 3; i++) await {|TF5100:count|};
    }

    async Task ValueTaskTwice()
    {
        ValueTask load = LoadAsync();
        await load;
        await {|TF5100:load|};
    }

    async UniTask ConvertedAfterAwait()
    {
        UniTask<int> count = CountAsync();
        await count;
        await {|TF5100:count|}.AsUniTask();
    }

    async UniTask AwaitedAfterConversion()
    {
        UniTask<int> count = CountAsync();
        UniTask converted = count.AsUniTask();
        await converted;
        await {|TF5100:count|};
    }

    async UniTask ObjectMethodsDontUseItUp()
    {
        UniTask<int> count = CountAsync();
        string text = count.ToString();
        await count;
    }

    async UniTask MaybeReassignedThroughAConditionalRef(bool replace)
    {
        UniTask<int> first = CountAsync();
        UniTask<int> second = CountAsync();
        await first;
        (replace ? ref first : ref second) = default;
        await {|TF5100:first|};
        await second;
    }
}");
        }

        [Test]
        public void TaskUsedOnceOnEachPath_IsFine()
        {
            AnalyzerVerifier.Verify(new TaskAwaitedTwiceAnalyzer(), @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

class Fader
{
    UniTask FadeAsync() => UniTask.CompletedTask;
    UniTask PlayAsync(List<int> steps) => default;

    async UniTask Branches(bool quick)
    {
        UniTask fade = FadeAsync();
        if (quick) fade.Forget();
        else await fade;
    }

    async UniTask Reassigned()
    {
        UniTask fade = FadeAsync();
        await fade;
        fade = FadeAsync();
        await fade;
    }

    async UniTask ReassignedFromACallWithAListInitializer()
    {
        UniTask playing = PlayAsync(new List<int> { 1 });
        await playing;
        playing = PlayAsync(new List<int> { 2 });
        playing.Forget();
    }

    async UniTask NewEachPass()
    {
        for (int i = 0; i < 3; i++)
        {
            UniTask fade = FadeAsync();
            await fade;
        }
    }

    async UniTask Preserved()
    {
        UniTask fade = FadeAsync().Preserve();
        await fade;
        await fade;
    }

    async UniTask EarlyReturn(bool skip)
    {
        UniTask fade = FadeAsync();
        if (skip)
        {
            await fade;
            return;
        }
        await fade;
    }

    async Task TasksCanBeAwaitedAgain()
    {
        Task load = Task.Delay(1);
        await load;
        await load;
    }

    async UniTask Lambda()
    {
        System.Func<UniTask> run = async () =>
        {
            UniTask fade = FadeAsync();
            await fade;
        };
        await run();
    }
}");
        }

        [Test]
        public void TokenBeforeOtherParameters_IsReported()
        {
            AnalyzerVerifier.Verify(new CancellationTokenNotLastAnalyzer(), @"
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;

delegate UniTask Step(CancellationToken {|TF5101:ct|}, int index);

interface IFader
{
    UniTask FadeAsync(CancellationToken {|TF5101:ct|}, float seconds);
}

class Fader : IFader
{
    Fader(CancellationToken ct, int id) { }

    public UniTask FadeAsync(CancellationToken ct, float seconds) => default;

    UniTask MoveAsync(CancellationToken {|TF5101:ct|}, float speed, int steps) => default;
    UniTask LastAsync(float speed, CancellationToken ct) => default;
    UniTask ParamsAsync(CancellationToken ct, params int[] steps) => default;
    bool TryLoad(CancellationToken ct, out int value) { value = 0; return true; }
    UniTask CallerAsync(CancellationToken ct, [CallerMemberName] string caller = null) => default;
    UniTask TwoTokensAsync(CancellationToken a, CancellationToken b) => default;
    UniTask OptionalAfterAsync(CancellationToken ct, int start = 0, string label = null) => default;

    void Local()
    {
        UniTask Inner(CancellationToken {|TF5101:ct|}, int n) => default;
    }
}

static class TokenExtensions
{
    public static void Register(this CancellationToken ct, int id) { }
}");
        }

        [Test]
        public void AwaitableWithoutSuffix_AndSuffixWithoutAwaitable_AreReported()
        {
            AnalyzerVerifier.Verify(new AsyncSuffixAnalyzer(), @"
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

class Door : MonoBehaviour
{
    UniTask {|TF5102:Open|}(CancellationToken ct) => default;
    UniTask<int> {|TF5102:Count|}() => default;
    Task {|TF5102:Save|}() => Task.CompletedTask;
    void {|TF5102:LoadAsync|}() { }
    int {|TF5102:CountAsync|}() => 0;

    UniTask OpenAsync(CancellationToken ct) => default;
    async UniTaskVoid Start() { await UniTask.Yield(); }
    async UniTaskVoid OnEnable() { await UniTask.Yield(); }
    async UniTaskVoid Slam() { await UniTask.Yield(); }
    async UniTaskVoid ShakeAsync() { await UniTask.Yield(); }
    async IAsyncEnumerable<int> StreamAsync() { await Task.Yield(); yield return 1; }
    Waiter SpecialAsync() => default;
    static Task Main() => Task.CompletedTask;
}

interface ILoader
{
    UniTask {|TF5102:Load|}();
}

class Loader : ILoader
{
    public UniTask Load() => default;
}

struct Waiter
{
    public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => default;
}");
        }

        [Test]
        public void TestMethods_KeepTheirNames()
        {
            AnalyzerVerifier.Verify(new AsyncSuffixAnalyzer(), @"
using System.Threading.Tasks;
using NUnit.Framework;

public class LoaderTests
{
    [Test]
    public async Task Load_ReturnsTheSave() { await Task.Yield(); }

    [SetUp]
    public Task Prepare() => Task.CompletedTask;

    Task {|TF5102:Helper|}() => Task.CompletedTask;
}", SnippetAssembly.Tests());
        }

        [Test]
        public void FireAndForgetMonoBehaviourAwaitWithoutAnyToken_IsReported()
        {
            AnalyzerVerifier.Verify(new MonoBehaviourAwaitWithoutTokenAnalyzer(), @"
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

class Hud : MonoBehaviour
{
    Task loading;

    async UniTaskVoid {|TF5103:Start|}()
    {
        await loading;
        enabled = false;
    }

    async UniTask {|TF5103:OnEnable|}()
    {
        await loading;
        enabled = false;
    }

    async UniTaskVoid {|TF5103:OnPlayClicked|}()
    {
        await loading;
        enabled = false;
    }

    async void {|TF5103:OnQuitClicked|}()
    {
        await loading;
        enabled = false;
    }

    async UniTaskVoid EndsOnItsAwait()
    {
        enabled = true;
        await loading;
        return;
    }

    async UniTaskVoid {|TF5103:InALoop|}()
    {
        for (int i = 0; i < 3; i++) await loading;
    }

    async UniTask ShowAsync()
    {
        await loading;
    }

    async UniTaskVoid WithToken()
    {
        await loading.AsUniTask().AttachExternalCancellation(destroyCancellationToken);
    }

    async UniTaskVoid WithParameter(CancellationToken ct)
    {
        await loading;
    }

    async UniTaskVoid CallThatTakesAToken()
    {
        await UniTask.Delay(100);
    }

    async UniTaskVoid NoAwait()
    {
    }

    static async UniTaskVoid Shared(Task work)
    {
        await work;
    }

    async UniTaskVoid LambdaOnly()
    {
        System.Func<Task> later = async () => await loading;
        await UniTask.CompletedTask.AttachExternalCancellation(destroyCancellationToken);
    }
}

class NotABehaviour
{
    async void Run(Task work) { await work; }
}

static class TaskConversions
{
    public static UniTask AsUniTask(this Task task) => default;
}");
        }

        [Test]
        public void AsyncLambdaAsVoidDelegate_IsReported()
        {
            AnalyzerVerifier.Verify(new AsyncLambdaAsVoidDelegateAnalyzer(), @"
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using UnityEngine.UIElements;

class Menu
{
    Task SaveAsync() => Task.CompletedTask;

    void Bind(Button button)
    {
        button.clicked += {|TF5104:async|} () => await SaveAsync();
        Action run = {|TF5104:async|} () => await SaveAsync();
        UnityAction unity = {|TF5104:async|} delegate { await SaveAsync(); };
        button.RegisterCallback<ClickEvent>({|TF5104:async|} evt => await SaveAsync());

        Func<Task> fine = async () => await SaveAsync();
        Func<UniTask> alsoFine = async () => await SaveAsync();
        button.clicked += UniTask.Action(async () => { await SaveAsync(); });
    }
}");
        }
    }
}