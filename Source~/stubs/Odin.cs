// Stub of the Odin Inspector types TF rules inspect, compiled as Sirenix.OdinInspector for tests.
using System;

namespace Sirenix.OdinInspector
{
    public class SerializedMonoBehaviour : UnityEngine.MonoBehaviour
    {
    }

    public class SerializedScriptableObject : UnityEngine.ScriptableObject
    {
    }

    public sealed class ShowIfAttribute : Attribute
    {
        public string Condition;
        public ShowIfAttribute(string condition, bool animate = true) { Condition = condition; }
    }

    public sealed class HideIfAttribute : Attribute
    {
        public HideIfAttribute(string condition, bool animate = true) { }
    }

    public sealed class EnableIfAttribute : Attribute
    {
        public EnableIfAttribute(string condition) { }
    }

    public sealed class DisableIfAttribute : Attribute
    {
        public DisableIfAttribute(string condition) { }
    }

    public sealed class ValueDropdownAttribute : Attribute
    {
        public ValueDropdownAttribute(string valuesGetter) { }
    }

    public sealed class ValidateInputAttribute : Attribute
    {
        public ValidateInputAttribute(string condition, string defaultMessage = null) { }
    }

    public sealed class OnValueChangedAttribute : Attribute
    {
        public OnValueChangedAttribute(string action) { }
    }

    public sealed class InlineButtonAttribute : Attribute
    {
        public InlineButtonAttribute(string action, string label = null) { }
    }

    public enum InfoMessageType
    {
        None,
        Info,
        Warning,
        Error
    }

    public sealed class InfoBoxAttribute : Attribute
    {
        public string VisibleIf;
        public InfoBoxAttribute(string message, InfoMessageType infoMessageType = InfoMessageType.Info, string visibleIfMemberName = null) { }
        public InfoBoxAttribute(string message, string visibleIfMemberName) { }
    }

    public sealed class LabelTextAttribute : Attribute
    {
        public LabelTextAttribute(string text) { }
    }

    public sealed class ReadOnlyAttribute : Attribute
    {
    }
}