using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    // TF5105 cancellation check the await already does.
    public class RedundantCancellationCheckTests
    {
        private const string Code = @"
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Cysharp.Threading.Tasks
{
    static class UnityAsyncExtensions
    {
        public static UniTask WithCancellation(this AsyncOperation operation, CancellationToken cancellationToken) => default;
    }
}

namespace UnityEngine
{
    static class Frames
    {
        public static UniTask NextAsync(CancellationToken cancellationToken) => default;
        public static UniTask<int> CountAsync(CancellationToken cancellationToken) => default;
    }
}

static class FaderExtensions
{
    public static UniTask WithCancellation(this UniTask task, CancellationToken ct) => task;
    public static UniTask<bool> SuppressCancellationThrow(this UniTask task) => default;
}

class Fader
{
    CancellationTokenSource cts;

    UniTask FadeAsync(CancellationToken ct) => UniTask.CompletedTask;
    UniTask<int> CountAsync() => default;
    UniTask LoadAsync() => UniTask.CompletedTask;

    async UniTask Run(CancellationToken ct, CancellationToken other, AsyncOperation loading)
    {
        await UniTask.Delay(500, ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        ct.ThrowIfCancellationRequested();
        await Frames.NextAsync(ct);

        int count = await CountAsync().AttachExternalCancellation(cancellationToken: ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        count = await CountAsync().AttachExternalCancellation(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await LoadAsync().AttachExternalCancellation(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await loading.WithCancellation(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await UniTask.NextFrame(cts.Token);
        cts.Token.ThrowIfCancellationRequested();

        // The project's own methods might return before looking at the token.
        await FadeAsync(ct);
        ct.ThrowIfCancellationRequested();

        ct.ThrowIfCancellationRequested();
        await FadeAsync(ct);

        await LoadAsync().WithCancellation(ct);
        ct.ThrowIfCancellationRequested();

        await LoadAsync();
        ct.ThrowIfCancellationRequested();

        await UniTask.Delay(500, other);
        ct.ThrowIfCancellationRequested();

        await UniTask.Delay(500, ct).SuppressCancellationThrow();
        ct.ThrowIfCancellationRequested();
        count++;

        await UniTask.Delay(500, ct);
        count++;
        ct.ThrowIfCancellationRequested();

        if (count > 0) ct.ThrowIfCancellationRequested();
        await UniTask.Delay(500, ct);

        {|TF5105:ct.ThrowIfCancellationRequested()|};
        await loading.WithCancellation(ct);
    }

    async UniTask<int> Returned(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Frames.CountAsync(ct);
    }

    // CountAsync runs before the wrapper looks at the token, so this check still stops it starting.
    async UniTask<int> WrappedProjectCall(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await CountAsync().AttachExternalCancellation(ct);
    }
}";

        [Test]
        public void CheckNextToAnAwaitWithTheSameToken_IsReported()
        {
            AnalyzerVerifier.Verify(new RedundantCancellationCheckAnalyzer(), Code);
        }

        [Test]
        public void Message_SaysWhichLineAlreadyThrows()
        {
            string[] messages = AnalyzerVerifier.Messages(new RedundantCancellationCheckAnalyzer(), @"
using System.Threading;
using Cysharp.Threading.Tasks;

class Fader
{
    async UniTask Run(CancellationToken ct)
    {
        await UniTask.Delay(500, ct);
        ct.ThrowIfCancellationRequested();
        int frames = 0;

        ct.ThrowIfCancellationRequested();
        await UniTask.Delay(500, ct);
        frames++;
    }
}");

            Assert.That(messages, Is.EqualTo(new[]
            {
                "The await on the previous line already throws when 'ct' is cancelled; remove this check",
                "The await on the next line already throws when 'ct' is cancelled; remove this check"
            }));
        }

        [Test]
        public void TestCode_IsCheckedToo()
        {
            AnalyzerVerifier.Verify(new RedundantCancellationCheckAnalyzer(), Code, SnippetAssembly.Tests());
        }
    }
}
