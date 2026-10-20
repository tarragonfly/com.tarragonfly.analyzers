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
    public async void Handle() { await Task.Delay(1); }
    public override async void Run() { await Task.Delay(1); }
    public async UniTaskVoid SpawnForget() { await UniTask.Yield(); }
    public async UniTask SpawnAsync() { await UniTask.Yield(); }

    void Subscribe()
    {
        Counted += (count) => { };
        Action lambda = async () => await Task.Delay(1);
    }
}");
        }

        // The method-group form of the UI Toolkit handler TF5104 catches as a lambda.
        [Test]
        public void AsyncVoidSubscribedToAnEvent_IsReportedWithTheWrapperAdvice()
        {
            Microsoft.CodeAnalysis.Diagnostic diagnostic = null;
            foreach (Microsoft.CodeAnalysis.Diagnostic reported in AnalyzerVerifier.Run(new AsyncVoidAnalyzer(), @"
using System;
using System.Threading.Tasks;

class PauseMenu
{
    event Action Clicked;

    async void OnPlayAsync() { await Task.Delay(1); }

    void Bind()
    {
        Clicked += OnPlayAsync;
    }
}"))
            {
                diagnostic = reported;
            }
            Assert.That(diagnostic?.GetMessage(), Does.Contain("() => OnPlayAsync().Forget()").And.Contain("UniTask.Action(OnPlayAsync)"));
        }
    }
}