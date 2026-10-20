using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class OverrideOnlyCallsBaseTests
    {
        private const string Base = @"
using System;

class Base
{
    public virtual void Update(int delta) { }
    public virtual int Score(int a) => a;
    public virtual int Health { get; set; }
    public virtual string Name => ""base"";
    public virtual void Defaults(int a = 1) { }
    public virtual void Twice(int a) { }
}
";

        [Test]
        public void PassThroughOverrides_AreReported()
        {
            AnalyzerVerifier.Verify(new OverrideOnlyCallsBaseAnalyzer(), Base + @"
class Derived : Base
{
    {|TF1011:public override void Update(int delta)
    {
        base.Update(delta);
    }|}

    {|TF1011:public override int Score(int a) => base.Score(a);|}

    {|TF1011:public override int Health
    {
        get { return base.Health; }
        set { base.Health = value; }
    }|}

    {|TF1011:public override string Name => base.Name;|}

    {|TF1011:public override string ToString() => base.ToString();|}
}");
        }

        [Test]
        public void OverridesThatChangeSomething_AreFine()
        {
            AnalyzerVerifier.Verify(new OverrideOnlyCallsBaseAnalyzer(), Base + @"
class Derived : Base
{
    [Obsolete]
    public override void Update(int delta) { base.Update(delta); }

    public sealed override int Score(int a) => base.Score(a);

    public override void Defaults(int a = 1) { base.Defaults(a); }

    public override void Twice(int a)
    {
        base.Twice(a + 1);
    }

    public override bool Equals(object obj) => base.Equals(obj);

    public override int GetHashCode() => base.GetHashCode();
}");
        }
    }
}