using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Inheritance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Inheritance
{
    // TF2506 event never raised.
    public class EventNeverRaisedTests
    {
        [Test]
        public void EventNothingRaises_IsReported()
        {
            AnalyzerVerifier.Verify(new EventNeverRaisedAnalyzer(), @"
using System;

interface INotifier
{
    event Action Changed;
}

abstract class Base
{
    public abstract event Action AbstractEvent;
}

class Player : Base, INotifier
{
    public event Action Died;
    public event Action<int> Scored;
    public event Action Passed;
    public event Action {|TF2506:Never|};
    private event Action {|TF2506:PrivateNever|};
    public static event Action {|TF2506:StaticNever|};
    public event Action {|TF2506:OnlyCleared|};
    public event Action {|TF2506:OnlyChecked|};
    public event Action Changed;
    public override event Action AbstractEvent;
    public event Action Custom { add { } remove { } }

    void Fire()
    {
        Died?.Invoke();
        Scored(1);
        Use(Passed);
        OnlyCleared = null;
        bool any = OnlyChecked != null;
        Never += () => { };
    }

    static void Use(Action action) => action();
}

class Nested
{
    public static event Action Raised;

    class Inner
    {
        void Fire() => Raised?.Invoke();
    }
}");
        }

        [Test]
        public void ForwardedHandlersAndRaisedOverrides_CountAsRaised()
        {
            AnalyzerVerifier.Verify(new EventNeverRaisedAnalyzer(), @"
using System;

class Clipboard
{
    public event Action BrushPicked;
    public event Action {|TF2506:Unforwarded|};
    public void Pick() => BrushPicked?.Invoke();
}

class ClipboardElement
{
    private readonly Clipboard clipboard = new Clipboard();
    public event Action BrushPicked;
    public void Attach()
    {
        clipboard.BrushPicked += BrushPicked;
        clipboard.Unforwarded += () => { };
    }
}

abstract class TextBase
{
    public virtual event Action<int> PreRender = delegate { };
    public virtual event Action<int> {|TF2506:PostRender|};
}

class TextMesh : TextBase
{
    public override event Action<int> PreRender;
    public void Render() => PreRender?.Invoke(1);
}");
        }
    }
}