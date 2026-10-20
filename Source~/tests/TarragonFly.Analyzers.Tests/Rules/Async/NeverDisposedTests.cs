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

        [Test]
        public void TempNativeCollectionsPointerViewsAndEmptyDispose_AreNotReported()
        {
            AnalyzerVerifier.Verify(new NeverDisposedAnalyzer(), @"
using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        private static int released;
        public NativeArray(int length, Allocator allocator) { }
        public int Length => 0;
        public void Dispose() { released++; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DeallocateOnJobCompletionAttribute : Attribute { }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public unsafe struct UnsafeList<T> : IDisposable where T : unmanaged
    {
        private static int released;
        public UnsafeList(T* items, int length) { }
        public void Dispose() { released++; }
    }
}

public sealed class Fetcher : IDisposable
{
    public Fetcher(object owner) { }
    public void Dispose() { }
}

public class Worker : IDisposable
{
    public virtual void Dispose() { }
}

public class Owner : Worker
{
    private int released;
    public override void Dispose() { released++; }
}

public struct TightRectJob
{
    [DeallocateOnJobCompletion] private NativeArray<int> widths;
    private NativeArray<int> heights;

    public static void Run(int count)
    {
        TightRectJob job = new TightRectJob();
        job.widths = new NativeArray<int>(count, Allocator.TempJob);
        {|TF5000:job.heights = new NativeArray<int>(count, Allocator.TempJob)|};
    }
}

public unsafe class Builder
{
    private CancellationTokenSource layer = new CancellationTokenSource();

    public bool Active { get; set; }

    public int Count(int size)
    {
        NativeArray<int> scratch = new NativeArray<int>(size, Allocator.Temp);
        NativeArray<int> {|TF5000:kept = new NativeArray<int>(size, Allocator.Persistent)|};
        int* items = stackalloc int[4];
        UnsafeList<int> view = new UnsafeList<int>(items, 4);
        Fetcher fetcher = new Fetcher(this);
        Worker {|TF5000:owner = new Owner()|};
        Worker worker = new Worker();
        return scratch.Length + kept.Length;
    }

    public void Begin() => Use(Active ? layer : null);

    private static void Use(CancellationTokenSource source) { }
}", new SnippetAssembly { AllowUnsafe = true });
        }

        [Test]
        public void OwningPointerWrapper_IsReported()
        {
            AnalyzerVerifier.Verify(new NeverDisposedAnalyzer(), @"
using System;
using System.Runtime.InteropServices;

public unsafe sealed class OwnedBuffer : IDisposable
{
    private byte* data;
    public OwnedBuffer(byte* data) { this.data = data; }
    public void Dispose() { Marshal.FreeHGlobal((IntPtr)data); data = null; }
}

public unsafe class Reader
{
    public int Leak()
    {
        byte* memory = (byte*)Marshal.AllocHGlobal(16);
        OwnedBuffer {|TF5000:buffer = new OwnedBuffer(memory)|};
        return buffer.GetHashCode();
    }
}", new SnippetAssembly { AllowUnsafe = true });
        }

        // Both live in package assemblies, so their Dispose bodies can't be read from here.
        [Test]
        public void PropertyFetcherAndShaderStringBuilder_AreNotReported()
        {
            SnippetAssembly assembly = new SnippetAssembly();
            assembly.ReferencedAssemblies["Library/Bee/artifacts/1900b0aE.dag/Unity.RenderPipelines.Core.Editor.ref.dll"] = @"
namespace UnityEditor.Rendering { public sealed class PropertyFetcher<T> : System.IDisposable { public PropertyFetcher(object owner) { } public void Dispose() { } } }
namespace UnityEditor.ShaderGraph { public class ShaderStringBuilder : System.IDisposable { int depth; public void Dispose() { depth--; } } }
namespace Tools { public class Handle : System.IDisposable { public void Dispose() { } } }";

            AnalyzerVerifier.Verify(new NeverDisposedAnalyzer(), @"
using UnityEditor.Rendering;
using UnityEditor.ShaderGraph;

class BloomEditor
{
    void OnEnable()
    {
        PropertyFetcher<int> fetcher = new PropertyFetcher<int>(this);
        ShaderStringBuilder builder = new ShaderStringBuilder();
        Tools.Handle {|TF5000:handle = new Tools.Handle()|};
    }
}", assembly);
        }
    }
}