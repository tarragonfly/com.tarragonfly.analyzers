// Stub of the UnityEditor types TF rules inspect, compiled as UnityEditor.CoreModule for tests.
using System;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnLoadMethodAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute
    {
    }

    public class Editor : UnityEngine.ScriptableObject
    {
    }
}