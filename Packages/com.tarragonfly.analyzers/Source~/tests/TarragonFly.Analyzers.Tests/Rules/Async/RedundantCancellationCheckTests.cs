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

static class CancellationExtensions
{
    public static UniTask WithCancellation(this UniTask task, CancellationToken ct) => task;
    public static UniTask<bool> SuppressCancellationThrow(this UniTask task) => default;
}

class Fader
{
    CancellationTokenSource cts;

    UniTask FadeAsync(CancellationToken ct) => UniTask.CompletedTask;
    UniTask<int> CountAsync(CancellationToken ct) => default;
    UniTask LoadAsync() => UniTask.CompletedTask;

    async UniTask Run(CancellationToken ct, CancellationToken other)
    {
        await UniTask.Delay(500, ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        {|TF5105:ct.ThrowIfCancellationRequested()|};
        await FadeAsync(ct);

        int count = await CountAsync(ct: ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        count = await CountAsync(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await LoadAsync().AttachExternalCancellation(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await LoadAsync().WithCancellation(ct);
        {|TF5105:ct.ThrowIfCancellationRequested()|};

        await FadeAsync(cts.Token);
        {|TF5105:cts.Token.ThrowIfCancellationRequested()|};

        await LoadAsync();
        ct.ThrowIfCancellationRequested();

        await FadeAsync(other);
        ct.ThrowIfCancellationRequested();

        await FadeAsync(ct).SuppressCancellationThrow();
        ct.ThrowIfCancellationRequested();
        count++;

        await FadeAsync(ct);
        count++;
        ct.ThrowIfCancellationRequested();

        if (count > 0) ct.ThrowIfCancellationRequested();
        await FadeAsync(ct);
    }

    async UniTask<int> Returned(CancellationToken ct)
    {
        {|TF5105:ct.ThrowIfCancellationRequested()|};
        return await CountAsync(ct);
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
