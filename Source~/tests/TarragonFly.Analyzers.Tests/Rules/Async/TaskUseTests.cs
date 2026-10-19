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
        await {|TF5005:UniTask.Yield|}();
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