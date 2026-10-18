// Stub of the Init(args) types TF rules inspect, compiled as InitArgs for tests. The real package has
// these up to twelve type arguments; two are enough here.
using System;

namespace Sisus.Init
{
    public interface IArgs<in TArgument>
    {
    }

    public interface IArgs<in TFirstArgument, in TSecondArgument>
    {
    }

    public interface IInitializable<in TArgument> : IArgs<TArgument>
    {
        void Init(TArgument argument);
    }

    public interface IInitializable<in TFirstArgument, in TSecondArgument> : IArgs<TFirstArgument, TSecondArgument>
    {
        void Init(TFirstArgument firstArgument, TSecondArgument secondArgument);
    }

    public abstract class MonoBehaviour<TArgument> : UnityEngine.MonoBehaviour, IInitializable<TArgument>
    {
        void IInitializable<TArgument>.Init(TArgument argument) => Init(argument);
        protected abstract void Init(TArgument argument);
    }

    public abstract class MonoBehaviour<TFirstArgument, TSecondArgument> : UnityEngine.MonoBehaviour, IInitializable<TFirstArgument, TSecondArgument>
    {
        void IInitializable<TFirstArgument, TSecondArgument>.Init(TFirstArgument firstArgument, TSecondArgument secondArgument) => Init(firstArgument, secondArgument);
        protected abstract void Init(TFirstArgument firstArgument, TSecondArgument secondArgument);
    }

    public abstract class ScriptableObject<TArgument> : UnityEngine.ScriptableObject, IInitializable<TArgument>
    {
        void IInitializable<TArgument>.Init(TArgument argument) => Init(argument);
        protected abstract void Init(TArgument argument);
    }

    [Serializable]
    public struct Any<T>
    {
        public T Value => default;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ServiceAttribute : UnityEngine.Scripting.PreserveAttribute
    {
        public ServiceAttribute() { }
        public ServiceAttribute(Type definingType) { }
    }

    public static class Service
    {
        public static T Get<T>() => default;
        public static void Set<T>(T instance) { }
    }

    public static class Service<TService>
    {
        public static TService Instance => default;
    }
}