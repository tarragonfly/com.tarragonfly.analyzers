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
    static MemoryStream shared = new MemoryStream();

    void Restart()
    {
        cancel.{|TF5001:Dispose|}();
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
        if (disposing) cancel.Dispose();
    }
}

class Plain
{
    MemoryStream stream = new MemoryStream();

    void Close() => stream.Dispose();
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
    }
}