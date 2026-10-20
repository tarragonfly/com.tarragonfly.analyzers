using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    // TF5003 blocking wait on a task, TF5004 null returned instead of a Task, TF5005 CancellationToken
    // not passed on.
    public class TaskUseTests
    {
        [Test]
        public void BlockingWaitOnATask_IsReported_UniTaskIncluded()
        {
            const string Code = @"
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

class Loader
{
    async Task Check(Task t, Task<int> ti, UniTask u, UniTask<int> uu)
    {
        {|TF5003:t.Wait|}();
        int a = {|TF5003:ti.Result|};
        {|TF5003:ti.GetAwaiter().GetResult|}();
        {|TF5003:u.GetAwaiter().GetResult|}();
        int b = {|TF5003:uu.GetAwaiter().GetResult|}();
        {|TF5003:Task.WaitAll|}(t);
        await t;
    }

    int Property => {|TF5003:Task.FromResult(1).Result|};

    static void Main()
    {
        Task.Delay(1).Wait();
    }
}";
            AnalyzerVerifier.Verify(new BlockingOnTaskAnalyzer(), Code);
            AnalyzerVerifier.Verify(new BlockingOnTaskAnalyzer(), Code.Replace("{|TF5003:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void ResultOfAFinishedTaskOrInNameof_IsNotReported()
        {
            AnalyzerVerifier.Verify(new BlockingOnTaskAnalyzer(), @"
using System.Threading.Tasks;

class Loader
{
    static void Use(int value) { }

    int AfterWait(Task<int> task)
    {
        {|TF5003:task.Wait|}();
        if (task.IsCompleted)
        {
            return task.Result;
        }
        return 0;
    }

    string Name() => nameof(Task<object>.Result);

    Task Forward(Task<int> task, Task<int> other)
    {
        return task.ContinueWith(done =>
        {
            Use(done.Result);
            Use({|TF5003:other.Result|});
        });
    }

    int NoWait(Task<int> task, Task<int> other)
    {
        {|TF5003:other.Wait|}();
        return {|TF5003:task.Result|};
    }
}");
        }

        [Test]
        public void ResultAfterAWait_IsFineOnlyWhenTheWaitProvesTheTaskFinished()
        {
            AnalyzerVerifier.Verify(new BlockingOnTaskAnalyzer(), @"
using System;
using System.Threading;
using System.Threading.Tasks;

class Loader
{
    static Task<int> LoadAsync() => Task.FromResult(1);

    int TokenWait(Task<int> task, CancellationToken ct)
    {
        {|TF5003:task.Wait|}(ct);
        return task.Result;
    }

    int ZeroWait(Task<int> task)
    {
        {|TF5003:task.Wait|}(0);
        return {|TF5003:task.Result|};
    }

    int TimedWait(Task<int> task)
    {
        {|TF5003:task.Wait|}(TimeSpan.FromSeconds(1));
        return {|TF5003:task.Result|};
    }

    int CheckedWait(Task<int> task)
    {
        if ({|TF5003:task.Wait|}(100))
        {
            return task.Result;
        }
        return {|TF5003:task.Result|};
    }

    int Reassigned(Task<int> task)
    {
        {|TF5003:task.Wait|}();
        task = LoadAsync();
        return {|TF5003:task.Result|};
    }

    Task<int> Continuation(Task<int> task) => task.ContinueWith(done => done.Result + 1);

    Task<int> ReassignedContinuation(Task<int> task) => task.ContinueWith(done =>
    {
        done = LoadAsync();
        return {|TF5003:done.Result|};
    });
}");
        }

        [Test]
        public void NullReturnedAsATask_IsReported()
        {
            AnalyzerVerifier.Verify(new NullTaskReturnedAnalyzer(), @"
using System;
using System.Threading.Tasks;

class Saver
{
    public Task Save() => {|TF5004:null|};
    public Task<int> Load() { return {|TF5004:null|}; }
    public Task Pending => {|TF5004:null|};

    public async Task<string> Name()
    {
        await Task.Yield();
        return null;
    }

    public Task Maybe(bool b) => b ? null : Task.CompletedTask;
    public Task<object> Default() => default;

    public Task Lambda()
    {
        Func<Task> make = () => null;
        return make();
    }

    public Task Parenthesized() => ({|TF5004:null|});
    public Task Getter { get { return (({|TF5004:null|})); } }

    public async Task Local()
    {
        Task Write() => {|TF5004:null|};
        static Task<int> Count() { return {|TF5004:null|}; }
        async Task Fine() { await Task.Yield(); }
        await Write();
    }
}");
        }

        [Test]
        public void CallLeavingOutTheTokenAtHand_IsReported()
        {
            AnalyzerVerifier.Verify(new CancellationTokenNotPassedAnalyzer(), @"
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

class Fader
{
    async UniTask FadeIn(CancellationToken ct)
    {
        await {|TF5005:UniTask.Delay|}(500);
        await {|TF5005:Task.Delay|}(10);
        await {|TF5005:Task.Delay|}(10, default);
        await {|TF5005:Helper|}();
        System.Func<Task> later = async () => await {|TF5005:Task.Delay|}(1);
        await UniTask.Delay(500, cancellationToken: ct);
        await Task.Delay(10, CancellationToken.None);
        await Helper(ct);
    }

    async UniTask NoToken()
    {
        await UniTask.Delay(500);
    }

    Task Helper() => Task.CompletedTask;
    Task Helper(CancellationToken ct) => Task.CompletedTask;
}

class WithSource
{
    readonly CancellationTokenSource source = new CancellationTokenSource();

    async Task Run()
    {
        await {|TF5005:Task.Delay|}(1);
    }
}");
        }

        [Test]
        public void TokenOverloadWithOptionalParametersAfterTheToken_Counts()
        {
            AnalyzerVerifier.Verify(new CancellationTokenNotPassedAnalyzer(), @"
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

static class Frames
{
    public static Task Pause(int frames) => Task.CompletedTask;
    public static Task Pause(int frames, CancellationToken cancellationToken, bool cancelImmediately = false) => Task.CompletedTask;
    public static Task Hold(int frames) => Task.CompletedTask;
    public static Task Hold(int frames, CancellationToken cancellationToken, bool cancelImmediately) => Task.CompletedTask;
    public static Task<int> Count() => Task.FromResult(0);
    public static Task<long> Count(CancellationToken cancellationToken) => Task.FromResult(0L);
}

class Scenario
{
    async UniTask Run(CancellationToken ct)
    {
        await {|TF5005:UniTask.NextFrame|}();
        await {|TF5005:UniTask.NextFrame|}().AttachExternalCancellation(ct);
        await {|TF5005:Frames.Pause|}(2);
        await Frames.Hold(2);
        await Frames.Count();
        await UniTask.Yield();
        await UniTask.NextFrame(ct);
        await UniTask.Yield(ct);
    }
}");
        }

        [Test]
        public void TokenOverloadForwardingToTheOneWithout_IsFine()
        {
            AnalyzerVerifier.Verify(new CancellationTokenNotPassedAnalyzer(), @"
using System;
using System.Threading;
using System.Threading.Tasks;

static class Client
{
    public static void Awaken(object client, CancellationToken lifetime)
    {
        Awaken(client);
        Action later = () => Awaken(client);
        {|TF5005:Save|}(client);
    }

    public static void Awaken(object client) { }

    public static void Save(object client) { }

    public static void Save(object client, CancellationToken token) { }
}");
        }

        [Test]
        public void MonoBehaviourWithoutAToken_UsesDestroyCancellationToken()
        {
            AnalyzerVerifier.Verify(new CancellationTokenNotPassedAnalyzer(), @"
using Cysharp.Threading.Tasks;
using UnityEngine;

class Door : MonoBehaviour
{
    async UniTask Open()
    {
        await {|TF5005:UniTask.Delay|}(500);
        await UniTask.Delay(500, cancellationToken: destroyCancellationToken);
        UniTask.Delay(500).Forget();
    }

    void OnDestroy()
    {
        System.Threading.Tasks.TaskCompletionSource<bool> request = new System.Threading.Tasks.TaskCompletionSource<bool>();
        request.TrySetCanceled();
    }

    static async UniTask Shared()
    {
        await UniTask.Delay(500);
    }
}");
        }
    }
}