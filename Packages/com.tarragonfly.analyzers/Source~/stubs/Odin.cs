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
        public ShowIfAttribute(string condition, object optionalValue, bool animate = true) { Condition = condition; }
    }

    public sealed class HideIfAttribute : Attribute
    {
        public HideIfAttribute(string condition, bool animate = true) { }
        public HideIfAttribute(string condition, object optionalValue, bool animate = true) { }
    }

    public sealed class EnableIfAttribute : Attribute
    {
        public EnableIfAttribute(string condition) { }
        public EnableIfAttribute(string condition, object optionalValue) { }
    }

    public sealed class DisableIfAttribute : Attribute
    {
        public DisableIfAttribute(string condition) { }
        public DisableIfAttribute(string condition, object optionalValue) { }
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

    // Group attributes, with Odin's own constructor parameter names, since TF6002 to TF6004 read the
    // group path by parameter name.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    public abstract class PropertyGroupAttribute : Attribute
    {
        public string GroupID;
        protected PropertyGroupAttribute(string groupId) { GroupID = groupId; }
        protected PropertyGroupAttribute(string groupId, float order) { GroupID = groupId; }
    }

    public sealed class BoxGroupAttribute : PropertyGroupAttribute
    {
        public BoxGroupAttribute(string group, bool showLabel = true, bool centerLabel = false, float order = 0) : base(group) { }
        public BoxGroupAttribute() : base("_DefaultBoxGroup") { }
    }

    public sealed class FoldoutGroupAttribute : PropertyGroupAttribute
    {
        public FoldoutGroupAttribute(string groupName, float order = 0) : base(groupName) { }
        public FoldoutGroupAttribute(string groupName, bool expanded, float order = 0) : base(groupName) { }
    }

    public sealed class HorizontalGroupAttribute : PropertyGroupAttribute
    {
        public HorizontalGroupAttribute(string group, float width = 0, int marginLeft = 0, int marginRight = 0, float order = 0) : base(group) { }
        public HorizontalGroupAttribute(float width = 0, int marginLeft = 0, int marginRight = 0, float order = 0) : base("_DefaultHorizontalGroup") { }
    }

    public sealed class VerticalGroupAttribute : PropertyGroupAttribute
    {
        public VerticalGroupAttribute(string groupId, float order = 0) : base(groupId) { }
        public VerticalGroupAttribute(float order = 0) : base("_DefaultVerticalGroup") { }
    }

    public sealed class TabGroupAttribute : PropertyGroupAttribute
    {
        public TabGroupAttribute(string tab, bool useFixedHeight = false, float order = 0) : base("_DefaultTabGroup") { }
        public TabGroupAttribute(string group, string tab, bool useFixedHeight = false, float order = 0) : base(group) { }
    }

    public sealed class ToggleGroupAttribute : PropertyGroupAttribute
    {
        public ToggleGroupAttribute(string toggleMemberName, float order = 0, string groupTitle = null) : base(toggleMemberName) { }
    }

    public sealed class TitleGroupAttribute : PropertyGroupAttribute
    {
        public TitleGroupAttribute(string title, string subtitle = null, bool horizontalLine = true, float order = 0) : base(title) { }
    }

    public sealed class ButtonGroupAttribute : PropertyGroupAttribute
    {
        public ButtonGroupAttribute(string group = "_DefaultGroup", float order = 0) : base(group) { }
    }

    public sealed class ShowIfGroupAttribute : PropertyGroupAttribute
    {
        public ShowIfGroupAttribute(string path, bool animate = true) : base(path) { }
    }

    public sealed class ColumnGroupAttribute : PropertyGroupAttribute
    {
        public ColumnGroupAttribute(string rowId, string columnId, float columnSize, float order = 0) : base(rowId) { }
        public ColumnGroupAttribute(string columnId) : base(columnId) { }
    }

    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    public sealed class ShowInInspectorAttribute : Attribute
    {
    }
}