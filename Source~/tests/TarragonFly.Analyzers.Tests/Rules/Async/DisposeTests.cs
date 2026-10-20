using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    // TF5001 field disposed outside Dispose.
    public class DisposeTests
    {
        [Test]
        public void FieldDisposedOutsideDispose_IsReported()
        {
            const string Code = @"
using System;
using System.IO;
using System.Threading;
using UnityEngine;

class Loader : IDisposable
{
    CancellationTokenSource cancel = new CancellationTokenSource();
    MemoryStream stream;
    MemoryStream log;
    static MemoryStream shared = new MemoryStream();

    void Restart()
    {
        cancel.{|TF5001:Dispose|}();
        cancel = new CancellationTokenSource();
        stream?.{|TF5001:Dispose|}();
        this.stream.{|TF5001:Dispose|}();
        Action later = () => cancel.{|TF5001:Dispose|}();
        MemoryStream local = new MemoryStream();
        local.Dispose();
        shared.Dispose();
    }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) log.Dispose();
    }
}

class Plain
{
    MemoryStream stream = new MemoryStream();

    void Close() => stream.Dispose();
}

class Resources : IDisposable
{
    internal MemoryStream lookup = new MemoryStream();
    public MemoryStream shown = new MemoryStream();
    protected MemoryStream cache = new MemoryStream();

    void DisposeResources()
    {
        lookup?.Dispose();
        lookup = null;
        shown.Dispose();
        cache.{|TF5001:Dispose|}();
    }

    public void Dispose() => DisposeResources();
}

class Fader : MonoBehaviour, IDisposable
{
    CancellationTokenSource cancel = new CancellationTokenSource();

    void OnDestroy() => cancel.Dispose();

    public void Dispose() { }
}";
            AnalyzerVerifier.Verify(new FieldDisposedOutsideDisposeAnalyzer(), Code);
            AnalyzerVerifier.Verify(new FieldDisposedOutsideDisposeAnalyzer(), Code.Replace("{|TF5001:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void FieldThatDisposeAlsoDisposes_IsFine()
        {
            AnalyzerVerifier.Verify(new FieldDisposedOutsideDisposeAnalyzer(), @"
using System;
using System.Threading;

class Grid : IDisposable
{
    CancellationTokenSource buffer = new CancellationTokenSource();
    CancellationTokenSource output;
    CancellationTokenSource pending;

    public void Resize()
    {
        buffer.Dispose();
        buffer = new CancellationTokenSource();
        output?.Dispose();
        output = null;
        pending.{|TF5001:Dispose|}();
        pending = new CancellationTokenSource();
    }

    public void Dispose()
    {
        buffer.Dispose();
        this.output?.Dispose();
        Release();
    }

    void Release() => pending.{|TF5001:Dispose|}();
}

class Pattern : IDisposable
{
    CancellationTokenSource cancel = new CancellationTokenSource();

    public void Restart() => cancel.Dispose();

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) cancel?.Dispose();
    }
}");
        }
    }
}