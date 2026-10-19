using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    // TF5000 disposable object never disposed.
    public class NeverDisposedTests
    {
        [Test]
        public void DisposableNeverDisposed_IsReported()
        {
            AnalyzerVerifier.Verify(new NeverDisposedAnalyzer(), @"
using System;
using System.IO;
using System.Threading;

class Fader : IDisposable
{
    private CancellationTokenSource {|TF5000:initialized = new CancellationTokenSource()|};
    private CancellationTokenSource disposedField = new CancellationTokenSource();
    private CancellationTokenSource fade;
    private CancellationTokenSource handedOver;
    private MemoryStream memory = new MemoryStream();

    public void Start()
    {
        {|TF5000:fade = new CancellationTokenSource()|};
        CancellationTokenSource {|TF5000:leaked = new CancellationTokenSource()|};
        leaked.Cancel();
        CancellationTokenSource {|TF5000:captured = new CancellationTokenSource()|};
        Action cancel = () => captured.Cancel();
        CancellationTokenSource {|TF5000:linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None)|};
        linked.Cancel();
        CancellationTokenSource disposed = new CancellationTokenSource();
        disposed?.Dispose();
        StreamReader closed = new StreamReader(""x"");
        closed.Close();
        using (CancellationTokenSource scoped = new CancellationTokenSource()) { scoped.Cancel(); }
        using CancellationTokenSource declared = new CancellationTokenSource();
        CancellationTokenSource stored = new CancellationTokenSource();
        handedOver = stored;
        CancellationTokenSource passed = new CancellationTokenSource();
        Store(passed);
        MemoryStream buffer = new MemoryStream();
        buffer.WriteByte(1);
    }

    public CancellationTokenSource Make()
    {
        CancellationTokenSource made = new CancellationTokenSource();
        return made;
    }

    private void Store(CancellationTokenSource source) => handedOver = source;

    public void Dispose() => disposedField.Dispose();
}");
        }
    }
}