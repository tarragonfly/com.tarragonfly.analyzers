// Stub of TarragonFly.TestBench, compiled as TarragonFly.TestBench. Only the members TF rules look
// for; bodies do nothing.
using System;
using UnityEngine;

namespace TarragonFly.TestBench
{
    public sealed class TestBench : IDisposable
    {
        public static Spawned<TestBench> Start(out TestBench bench) { bench = new TestBench(); return new Spawned<TestBench>(); }
        public static Spawned<TSeed> Start<TSeed>(TSeed seed, out TestBench bench) where TSeed : class { bench = new TestBench(); return new Spawned<TSeed>(); }
        public GameObject NewGameObject(string name, bool active = true) => null;
        public void AtTearDown(Action undo) { }
        public void Dispose() { }
    }

    public sealed class Spawned<TLast>
    {
        public Spawned<TParent[]> Under<TParent>(TParent parent) where TParent : class => null;
    }

    public static class Serialized
    {
        public static void Set(UnityEngine.Object target, string field, object value) { }
    }

    public static class PrivateField
    {
        public static void Set(object owner, string name, object value) { }
        public static T Get<T>(object owner, string name) => default;
    }
}
