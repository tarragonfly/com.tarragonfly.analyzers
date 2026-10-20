using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2024 format that throws, TF2053 unused format argument, TF2056 base call dropping an optional
    // argument, TF2060 wrong parameter name, TF2066 empty Guid, TF2067 invalid regex.
    public class ArgumentTests
    {
        [Test]
        public void FormatThatThrows_IsReported()
        {
            AnalyzerVerifier.Verify(new FormatThatThrowsAnalyzer(), @"
using System;
using System.Text;
using UnityEngine;

class Target
{
    void Check(int a, StringBuilder builder, object[] values)
    {
        string r1 = {|TF2024:string.Format|}(""{0} {1}"", a);
        string r2 = {|TF2024:string.Format|}(""{0"", a);
        {|TF2024:Console.WriteLine|}(""{name}"", a);
        {|TF2024:Debug.LogFormat|}(""{1}"", a);
        {|TF2024:builder.AppendFormat|}(""{2}"", a);
        string format = ""{3}"";
        string r3 = {|TF2024:string.Format|}(format, a);
        string fine1 = string.Format(""{0} {0}"", a);
        string fine2 = string.Format(""{0} {1}"", values);
        string fine3 = string.Format(""{{0}}"");
    }
}");
        }

        [Test]
        public void FormatItems_AreReadLikeTheRuntimeReadsThem()
        {
            AnalyzerVerifier.Verify(new FormatThatThrowsAnalyzer(), @"
class Target
{
    static void Rewrite(ref string format) => format = ""{0}"";

    void Check(int a, int b)
    {
        string r1 = {|TF2024:string.Format|}(""{0,}"", a);
        string r2 = {|TF2024:string.Format|}(""{0,+5}"", a);
        string r3 = {|TF2024:string.Format|}(""{ 0}"", a);
        string r4 = {|TF2024:string.Format|}(""{0,5,6}"", a);
        string r5 = {|TF2024:string.Format|}(""{0:x{}"", a);
        string r6 = {|TF2024:string.Format|}(arg0: a, format: ""{1}"");
        string fine1 = string.Format(""{0:}"", a);
        string fine2 = string.Format(""{0 }"", a);
        string fine3 = string.Format(""{0, -5}"", a);
        string fine4 = string.Format(""{0:}}}"", a);
        string fine5 = string.Format(""({0})"", (a > b ? a : b));
        string format = ""{1}"";
        Rewrite(ref format);
        string fine6 = string.Format(format, a);
    }
}");
        }

        [Test]
        public void FormatArgumentNoPlaceholderUses_IsReported()
        {
            AnalyzerVerifier.Verify(new UnusedFormatArgumentAnalyzer(), @"
using System;
using UnityEngine;

class Target
{
    void Check(int a, int b, int c, object[] values)
    {
        string r1 = {|TF2053:string.Format|}(""{0} hp"", a, b);
        string r2 = {|TF2053:string.Format|}(""Done"", a);
        string r3 = {|TF2053:string.Format|}(""{0} {2}"", a, b, c);
        string r4 = {|TF2053:string.Format|}(""Done"");
        {|TF2053:Debug.LogFormat|}(""{0}"", a, b);
        string fine1 = string.Format(""{0} {0}"", a);
        string fine2 = string.Format(""{0}"");
        string fine3 = string.Format(""{0} {1}"", a);
        string fine4 = string.Format(""Done"", values);
        Console.WriteLine(""Done"");
    }
}");
        }

        [Test]
        public void BaseCallLeavingOutOptionalArgument_IsReported()
        {
            AnalyzerVerifier.Verify(new BaseCallDropsOptionalArgumentAnalyzer(), @"
class Unit
{
    public virtual void TakeDamage(int amount, bool critical = false, string source = null) { }
    public virtual void Heal(int amount = 1) { }
}

class Boss : Unit
{
    public override void TakeDamage(int amount, bool critical = false, string source = null)
    {
        {|TF2056:base.TakeDamage(amount)|};
    }

    public override void Heal(int amount = 1)
    {
        base.Heal(amount);
    }
}

class Archer : Unit
{
    public override void TakeDamage(int amount, bool critical = false, string source = null)
    {
        {|TF2056:base.TakeDamage(amount, source: source)|};
        base.Heal();
    }
}

class Knight : Unit
{
    public override void TakeDamage(int amount, bool critical = false, string source = null)
    {
        base.TakeDamage(amount, critical, source: source);
    }
}");
        }

        [Test]
        public void ArgumentExceptionNamingAMissingParameter_IsReported()
        {
            AnalyzerVerifier.Verify(new WrongParameterNameAnalyzer(), @"
using System;

class Target
{
    string name;

    public string Name
    {
        get => name;
        set => name = value ?? throw new ArgumentNullException(""value"");
    }

    public string Title
    {
        set => throw new ArgumentException(""read only"", ""Title"");
    }

    int this[int slot] => slot < 0 ? throw new ArgumentOutOfRangeException(""slot"") : slot;

    void Follow(object player)
    {
        if (player == null) throw {|TF2060:new ArgumentNullException(""target"")|};
        if (player == null) throw {|TF2060:new ArgumentOutOfRangeException(""index"", ""bad"")|};
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (player == null) throw new ArgumentException(""bad"", ""player"");
        if (player == null) throw new ArgumentNullException(paramName: ""player"", message: ""missing"");
        if (player == null) throw new InvalidOperationException(""target"");
        Action<int> check = index => { if (index < 0) throw new ArgumentOutOfRangeException(nameof(index)); };
    }
}");
        }

        [Test]
        public void ArgumentExceptionNamingNoParameter_IsReported()
        {
            AnalyzerVerifier.Verify(new WrongParameterNameAnalyzer(), @"
using System;

class CustomArgumentException : ArgumentException { }

class Target
{
    enum Mode { Walk, Run }

    int Speed(Mode mode)
    {
        switch (mode)
        {
            case Mode.Walk: return 1;
            case Mode.Run: return 2;
            default: throw {|TF2060:new ArgumentOutOfRangeException()|};
        }
    }

    void Check(object target, Exception inner)
    {
        if (target == null) throw {|TF2060:new ArgumentNullException()|};
        if (target == null) throw {|TF2060:new ArgumentException()|};
        if (target == null) throw {|TF2060:new ArgumentNullException(null)|};
        if (target == null) throw {|TF2060:new DuplicateWaitObjectException()|};
        if (target == null) throw {|TF2060:new ArgumentOutOfRangeException { }|};
        ArgumentOutOfRangeException later = {|TF2060:new()|};
        if (target == null) throw new ArgumentException(""bad"");
        if (target == null) throw new ArgumentNullException(""bad"", inner);
        if (target == null) throw new CustomArgumentException();
    }
}");
        }

        [Test]
        public void EmptyGuidThatLooksNew_IsReported()
        {
            AnalyzerVerifier.Verify(new EmptyGuidAnalyzer(), @"
using System;

class Target
{
    Guid id = {|TF2066:new Guid()|};

    void Check()
    {
        Guid r1 = {|TF2066:default(Guid)|};
        Guid r2 = {|TF2066:default|};
        Guid r3 = {|TF2066:new Guid(""00000000-0000-0000-0000-000000000000"")|};
        Guid r4 = Guid.NewGuid();
        Guid r5 = Guid.Empty;
        Guid r6 = new Guid(""6f9619ff-8b86-d011-b42d-00c04fc964ff"");
        int r7 = default;
    }
}");
        }

        [Test]
        public void DefaultAsAParameterDefault_IsFine()
        {
            AnalyzerVerifier.Verify(new EmptyGuidAnalyzer(), @"
using System;

class Target
{
    void A(Guid id = default) { }
    void B(Guid id = default(Guid)) { }
    void C(Guid id = {|TF2066:new Guid()|}) { }
    Guid D(Guid fallback = default) => fallback == {|TF2066:default|} ? Guid.NewGuid() : fallback;
}

record Save(Guid Id = default);");
        }

        [Test]
        public void RegexThatDoesNotParse_IsReported()
        {
            AnalyzerVerifier.Verify(new InvalidRegexAnalyzer(), @"
using System.Text.RegularExpressions;

class Target
{
    void Check(string name, string pattern)
    {
        Regex r1 = new Regex({|TF2067:""[0-9""|});
        bool r2 = Regex.IsMatch(name, {|TF2067:""(enemy""|});
        string r3 = Regex.Replace(name, {|TF2067:""*""|}, """");
        Regex r4 = new Regex({|TF2067:""a{2,1}""|}, RegexOptions.IgnoreCase);
        Regex r5 = new Regex(""[0-9]+"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        bool r6 = Regex.IsMatch(name, pattern);
        bool r7 = new Regex(""x"").IsMatch(""(enemy"");
    }
}");
        }

        [Test]
        public void RegexPatternFromReadonlyFieldOrLocal_IsChecked()
        {
            AnalyzerVerifier.Verify(new InvalidRegexAnalyzer(), @"
using System.Text.RegularExpressions;

class Target
{
    static readonly string Broken = ""["";
    static readonly string Changed = ""["";
    readonly string valid = ""[a-z]"";
    static string notReadonly = ""["";

    static Target()
    {
        Changed = ""[a]"";
    }

    void Check()
    {
        string local = ""(abc"";
        string reassigned = ""(abc"";
        reassigned = ""abc"";
        Regex r1 = new Regex({|TF2067:Broken|});
        Regex r2 = new Regex({|TF2067:local|});
        Regex r3 = new Regex(Changed);
        Regex r4 = new Regex(valid);
        Regex r5 = new Regex(notReadonly);
        Regex r6 = new Regex(reassigned);
    }
}");
        }

        [Test]
        public void RegexFeatureTheOptionsCannotRun_IsNotReported()
        {
            AnalyzerVerifier.Verify(new InvalidRegexAnalyzer(), @"
using System.Text.RegularExpressions;

class Target
{
    void Check()
    {
        Regex r1 = new Regex(@""(a)\1"", RegexOptions.NonBacktracking);
        Regex r2 = new Regex(""(?=a)a"", RegexOptions.NonBacktracking);
    }
}");
        }
    }
}