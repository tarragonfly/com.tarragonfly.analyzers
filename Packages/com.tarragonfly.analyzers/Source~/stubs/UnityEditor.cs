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

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InitializeOnEnterPlayModeAttribute : Attribute
    {
    }

    public class Editor : UnityEngine.ScriptableObject
    {
    }

    public class AssetPostprocessor
    {
    }

    public class AssetModificationProcessor
    {
    }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();

        public static CallbackFunction delayCall;
    }

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static void SaveAssets() { }
        public static bool DeleteAsset(string path) => false;
    }

    public static class Undo
    {
        public static void RecordObject(UnityEngine.Object target, string name) { }
        public static void DestroyObjectImmediate(UnityEngine.Object target) { }
    }
}