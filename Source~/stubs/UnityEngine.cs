// Stub of the UnityEngine types TF rules inspect, compiled as UnityEngine.CoreModule for tests.
// Only the shape matters; bodies do nothing.
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object target) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public static implicit operator bool(Object target) => !ReferenceEquals(target, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour
    {
        public void StartCoroutine(System.Collections.IEnumerator routine) { }
    }

    public class ScriptableObject : Object
    {
    }

    public class StateMachineBehaviour : ScriptableObject
    {
    }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public T GetComponent<T>() => default;
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReference : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ContextMenu : Attribute
    {
        public ContextMenu(string itemName) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public sealed class ContextMenuItemAttribute : Attribute
    {
        public ContextMenuItemAttribute(string name, string function) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
    }
}

namespace UnityEngine.Scripting
{
    public class PreserveAttribute : Attribute
    {
    }
}

namespace JetBrains.Annotations
{
    public sealed class UsedImplicitlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MeansImplicitUseAttribute : Attribute
    {
    }
}