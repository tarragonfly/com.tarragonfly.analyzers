// Stub of the UnityEngine types TF rules inspect, compiled as UnityEngine.CoreModule for tests.
// Only the shape matters; bodies do nothing.
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object target) { }
        public static T FindObjectOfType<T>() where T : Object => null;
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsOfType<T>() where T : Object => null;
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
        public System.Threading.CancellationToken destroyCancellationToken => default;
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
        public static GameObject Find(string name) => null;
        public static GameObject FindWithTag(string tag) => null;
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 normalized => this;
        public void Normalize() { }
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
    }

    public static class Mathf
    {
        public static float Abs(float value) => value;
        public static float Clamp(float value, float min, float max) => value;
        public static float Max(float a, float b) => a;
        public static float Min(float a, float b) => a;
        public static float Sqrt(float value) => value;
        public static float Lerp(float a, float b, float t) => a;
    }

    public enum LogType
    {
        Error,
        Assert,
        Warning,
        Log,
        Exception,
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogAssertion(object message) { }
        public static void LogException(Exception exception) { }
        public static void LogFormat(string format, params object[] args) { }
        public static void LogFormat(Object context, string format, params object[] args) { }
        public static void LogWarningFormat(string format, params object[] args) { }
        public static void LogErrorFormat(string format, params object[] args) { }
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

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public abstract class PropertyAttribute : Attribute
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

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
}

namespace UnityEngine.SceneManagement
{
    public static class SceneManager
    {
        public static void LoadScene(string sceneName) { }
        public static AsyncOperation LoadSceneAsync(string sceneName) => null;
    }
}

namespace UnityEngine
{
    public class AsyncOperation
    {
    }
}

namespace UnityEngine.UIElements
{
    public delegate void EventCallback<in TEventType>(TEventType evt);

    public enum TrickleDown
    {
        NoTrickleDown,
        TrickleDown
    }

    public class EventBase
    {
    }

    public class ClickEvent : EventBase
    {
    }

    public class ChangeEvent<T> : EventBase
    {
        public T newValue => default;
    }

    public interface INotifyValueChanged<T>
    {
        T value { get; set; }
    }

    public abstract class CallbackEventHandler
    {
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase { }
        public void UnregisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase { }
    }

    public class VisualElement : CallbackEventHandler
    {
    }

    public class Button : VisualElement
    {
        public event Action clicked;
        public void Click() => clicked?.Invoke();
    }

    public class Toggle : VisualElement, INotifyValueChanged<bool>
    {
        public bool value { get; set; }
    }

    public static class INotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback) => true;
        public static bool UnregisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback) => true;
    }
}

namespace Unity.Scripting.LifecycleManagement
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    public sealed class AutoStaticsCleanupAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    public sealed class NoAutoStaticsCleanupAttribute : Attribute
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