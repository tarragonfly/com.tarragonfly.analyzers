using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Async;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Async
{
    public class AsyncVoidTests
    {
        [Test]
        public void AsyncVoidMethods_AreReportedOnVoid_UnityMessagesIncluded()
        {
            AnalyzerVerifier.Verify(new AsyncVoidAnalyzer(), @"
using Cysharp.Threading.Tasks;
using UnityEngine;

class Spawner : MonoBehaviour
{
    async {|TF5002:void|} Start() { await UniTask.Yield(); }
    public async {|TF5002:void|} Spawn() { await UniTask.Yield(); }
}");
        }

        [Test]
        public void EventHandlersFixedSignaturesAndUniTask_AreFine()
        {
            AnalyzerVerifier.Verify(new AsyncVoidAnalyzer(), @"
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

interface IHandler
{
    void Handle();
}

abstract class Base
{
    public abstract void Run();
}

class Spawner : Base, IHandler
{
    event Action<int> Counted;

    async void OnSpawned(object sender, EventArgs e) { await Task.Delay(1); }
    async void OnCounted(int count) { await Task.Delay(1); }
    public async void Handle() { await Task.Delay(1); }
    public override async void Run() { await Task.Delay(1); }
    public async UniTaskVoid SpawnForget() { await UniTask.Yield(); }
    public async UniTask SpawnAsync() { await UniTask.Yield(); }

    void Subscribe()
    {
        Counted += OnCounted;
        Action lambda = async () => await Task.Delay(1);
    }
}");
        }
    }
}