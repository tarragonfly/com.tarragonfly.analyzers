// Stub of UniTask (Cysharp), compiled as UniTask. Awaiting works so async methods compile.
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(CompilerServices.AsyncUniTaskMethodBuilder))]
    public readonly struct UniTask
    {
        public static UniTask CompletedTask => default;
        public static UniTask Delay(int milliseconds, CancellationToken cancellationToken = default) => default;
        public static UniTask WaitUntilCanceled(CancellationToken cancellationToken) => default;
        public static IEnumerator ToCoroutine(Func<UniTask> routine) => null;
        public static YieldAwaitable Yield() => default;
        public static UniTask Yield(CancellationToken cancellationToken, bool cancelImmediately = false) => default;
        public static UniTask NextFrame() => default;
        public static UniTask NextFrame(CancellationToken cancellationToken, bool cancelImmediately = false) => default;
        public static UniTask WhenAll(params UniTask[] tasks) => default;
        public static Action Action(Func<UniTaskVoid> asyncAction) => null;
        public static void Void(Func<UniTaskVoid> asyncAction) { }
        public Awaiter GetAwaiter() => default;

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            public bool IsCompleted => true;
            public void GetResult() { }
            public void OnCompleted(Action continuation) { }
            public void UnsafeOnCompleted(Action continuation) { }
        }
    }

    public readonly struct YieldAwaitable
    {
        public Awaiter GetAwaiter() => default;
        public UniTask ToUniTask() => default;

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            public bool IsCompleted => false;
            public void GetResult() { }
            public void OnCompleted(Action continuation) { }
            public void UnsafeOnCompleted(Action continuation) { }
        }
    }

    [AsyncMethodBuilder(typeof(CompilerServices.AsyncUniTaskMethodBuilder<>))]
    public readonly struct UniTask<T>
    {
        public Awaiter GetAwaiter() => default;
        public UniTask AsUniTask() => default;

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            public bool IsCompleted => true;
            public T GetResult() => default;
            public void OnCompleted(Action continuation) { }
            public void UnsafeOnCompleted(Action continuation) { }
        }
    }

    [AsyncMethodBuilder(typeof(CompilerServices.AsyncUniTaskVoidMethodBuilder))]
    public readonly struct UniTaskVoid
    {
        public void Forget() { }
    }

    public static class UniTaskExtensions
    {
        public static void Forget(this UniTask task) { }
        public static void Forget<T>(this UniTask<T> task) { }
        public static UniTask Preserve(this UniTask task) => task;
        public static UniTask<T> Preserve<T>(this UniTask<T> task) => task;
        public static UniTask AttachExternalCancellation(this UniTask task, CancellationToken cancellationToken) => task;
        public static UniTask<T> AttachExternalCancellation<T>(this UniTask<T> task, CancellationToken cancellationToken) => task;
        public static IEnumerator ToCoroutine(this UniTask task) => null;
    }
}

namespace Cysharp.Threading.Tasks.CompilerServices
{
    public struct AsyncUniTaskMethodBuilder
    {
        public static AsyncUniTaskMethodBuilder Create() => default;
        public UniTask Task => default;
        public void SetException(Exception exception) { }
        public void SetResult() { }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }

    public struct AsyncUniTaskMethodBuilder<T>
    {
        public static AsyncUniTaskMethodBuilder<T> Create() => default;
        public UniTask<T> Task => default;
        public void SetException(Exception exception) { }
        public void SetResult(T result) { }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }

    public struct AsyncUniTaskVoidMethodBuilder
    {
        public static AsyncUniTaskVoidMethodBuilder Create() => default;
        public UniTaskVoid Task => default;
        public void SetException(Exception exception) { }
        public void SetResult() { }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}
