using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Performance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Performance
{
    // TF8002 string concatenation in a loop, TF8003 property copying a collection, TF8005
    // ToCharArray not needed, TF8006 array built for params, TF8008 Substring then IndexOf, TF8009
    // DateTime.Now for timing.
    public class AllocationTests
    {
        [Test]
        public void StringBuiltWithPlusInALoop_IsReported()
        {
            const string Code = @"
using System.Collections.Generic;

class Target
{
    string log = """";

    string Check(string[] names, List<string> list)
    {
        string all = """";
        foreach (string n in names) {|TF8002:all += n|};
        for (int i = 0; i < 3; i++) {|TF8002:all = all + i|};
        while (all.Length < 10) {|TF8002:all = ""x"" + all|};
        foreach (string n in names) {|TF8002:log += n|};
        foreach (string n in names)
        {
            string inner = """";
            inner += n;
            for (int i = 0; i < 2; i++) {|TF8002:inner += n|};
        }
        foreach (string n in names)
        {
            System.Action add = () => {|TF8002:all += n|};
        }
        foreach (string n in names) all = n + ""!"";
        foreach (string n in names) all = $""{all}{n}"";
        int total = 0;
        foreach (string n in names) total += n.Length;
        list.ForEach(n => all += n);
        return all;
    }
}";
            AnalyzerVerifier.Verify(new StringConcatInLoopAnalyzer(), Code);
            AnalyzerVerifier.Verify(new StringConcatInLoopAnalyzer(), Code.Replace("{|TF8002:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void FieldOfTheItemEachPassWorksOn_IsNotCounted()
        {
            AnalyzerVerifier.Verify(new StringConcatInLoopAnalyzer(), @"
using System.Collections.Generic;

class Group
{
    public string Key;
}

class Sections
{
    Group shared = new Group();

    void Prefix(List<Group> groups, string section)
    {
        foreach (Group group in groups)
        {
            group.Key = section + ""/"" + group.Key;
            Group copy = group;
            copy.Key += ""!"";
            {|TF8002:shared.Key += section|};
            {|TF8002:this.shared.Key = shared.Key + section|};
        }
    }
}");
        }

        [Test]
        public void PropertyThatCopiesACollection_IsReported()
        {
            AnalyzerVerifier.Verify(new PropertyCopiesCollectionAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;

class Inventory
{
    List<int> items = new List<int>();
    int[] array = new int[1];

    public List<int> Copy => {|TF8003:new List<int>(items)|};
    public List<int> Listed { get { return {|TF8003:items.ToList|}(); } }
    public int[] Cloned => (int[]){|TF8003:array.Clone|}();
    public int[] FromArray => {|TF8003:array.ToArray|}();
    public List<int> Maybe => items.Count > 0 ? {|TF8003:items.ToList|}() : null;
    public int[] InstanceToArray => items.ToArray();
    public IEnumerable<int> Lazy => items.Where(x => x > 0);
    public List<int> Direct => items;
    public List<int> Empty => new List<int>();
    public List<int> this[int index] => items.ToList();
    public int[] Method() => items.ToArray().ToArray();

    [ShowInInspector]
    public List<int> Shown => new List<int>(items);
}");
        }

        [Test]
        public void CachedCopiesViewsAndNonCollectionProperties_AreFine()
        {
            AnalyzerVerifier.Verify(new PropertyCopiesCollectionAnalyzer(), @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

class Shaders
{
    List<string> names = new List<string>();
    string[] scopes;
    string[] sorted;

    public string[] Scopes => scopes ??= names.ToArray();
    public string[] Sorted
    {
        get
        {
            if (sorted == null) sorted = names.OrderBy(n => n).ToArray();
            return sorted;
        }
    }
    public ReadOnlyCollection<string> View => new ReadOnlyCollection<string>(names);
    public bool AllSame
    {
        get
        {
            List<string> copy = names.ToList();
            return copy.Distinct().Count() == 1;
        }
    }
    public List<string> Fresh => {|TF8003:names.ToList|}();
}");
        }

        [Test]
        public void OnlyCopiesTheGetterReturns_AreReported()
        {
            AnalyzerVerifier.Verify(new PropertyCopiesCollectionAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Inventory
{
    List<int> items = new List<int>();

    static int Count(List<int> values) => values.Count;

    public object Boxed => {|TF8003:items.ToList|}();
    public bool Any => {|TF8003:items.ToList|}().Count > 0;
    public List<int> Wrapped => {|TF8003:new List<int>(items.ToList())|};
    public int Counted => Count(items.ToList());
    public string Joined => string.Join("","", items.ToArray());
    public List<int> ViaLocal { get { List<int> copy = items.ToList(); return copy; } }
    public IEnumerable<int> Looped { get { foreach (int item in items.ToList()) { } return null; } }
}");
        }

        [Test]
        public void ToCharArrayOnlyToLoopOrIndex_IsReported()
        {
            AnalyzerVerifier.Verify(new ToCharArrayNotNeededAnalyzer(), @"
class Target
{
    void Check(string line)
    {
        foreach (char c in line.{|TF8005:ToCharArray|}()) { }
        char first = line.{|TF8005:ToCharArray|}()[0];
        char[] kept = line.ToCharArray();
        int length = line.ToCharArray().Length;
        foreach (char c in line.ToCharArray(0, 2)) { }
    }
}");
        }

        [Test]
        public void ArrayBuiltForParams_IsReported()
        {
            AnalyzerVerifier.Verify(new ArrayForParamsAnalyzer(), @"
class Target
{
    void Spawn(params int[] ids) { }
    void Log(params object[] values) { }
    void Named(string name, params string[] tags) { }
    void Fixed(int[] ids) { }

    void Check(int[] existing)
    {
        Spawn({|TF8006:new[] { 1, 2 }|});
        Spawn({|TF8006:new int[] { }|});
        Named(""x"", {|TF8006:tags: new string[] { ""a"" }|});
        Log({|TF8006:new[] { ""a"", ""b"" }|});
        Log(new object[] { ""a"" });
        Spawn(new int[0]);
        Spawn(existing);
        Spawn(1, 2);
        Fixed(new[] { 1 });
    }
}");
        }

        [Test]
        public void ArraysWhoseItemsWouldCallTheMemberItselfOrNotCompile_AreFine_AndAttributesAreChecked()
        {
            AnalyzerVerifier.Verify(new ArrayForParamsAnalyzer(), @"
using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
sealed class FilterAttribute : Attribute
{
    public FilterAttribute(params Type[] types) { }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
sealed class LabelAttribute : Attribute
{
    public LabelAttribute(string name, string group = """", params Type[] types) { }
}

[Filter({|TF8006:new[] { typeof(Target), typeof(string) }|})]
[Label(""x"", types: new[] { typeof(Target), typeof(string) })]
[Label(""x"", {|TF8006:types: new[] { typeof(Target) }|})]
class Target
{
    static void Foo(int a) { }
    static void Foo(params int[] all) { }
    static void Bar(int a, params int[] rest) { }
    static void Baz(int a, int b = 0, params int[] rest) { }

    Target(int a) : this(new[] { a }) { }
    Target(params int[] all) { }

    int this[params int[] keys] => 0;

    void Run()
    {
        Foo(new[] { 1 });
        Foo({|TF8006:new[] { 1, 2 }|});
        Bar(1, {|TF8006:rest: new[] { 2, 3 }|});
        Baz(1, rest: new[] { 2, 3 });
        int value = this[{|TF8006:new[] { 1, 2 }|}];
    }
}");
        }

        [Test]
        public void IndexOfOnSubstring_IsReported()
        {
            AnalyzerVerifier.Verify(new SubstringThenIndexOfAnalyzer(), @"
class Target
{
    void Check(string line, int start)
    {
        int r1 = {|TF8008:line.Substring(start).IndexOf(':')|};
        int r2 = {|TF8008:line.Substring(start, 3).LastIndexOf(""x"")|};
        int r3 = {|TF8008:line.Substring(start).IndexOfAny(new[] { 'x' })|};
        bool r4 = line.Substring(start).StartsWith(""x"");
        int r5 = line.IndexOf(':', start);
        int r6 = line?{|TF8008:.Substring(start).IndexOf(':')|} ?? -1;
        int? r7 = line.Substring(start)?.IndexOf(':');
    }
}");
        }

        [Test]
        public void ElapsedTimeFromDateTimeNow_IsReported()
        {
            AnalyzerVerifier.Verify(new DateTimeNowTimingAnalyzer(), @"
using System;

class Target
{
    void Check(DateTime started)
    {
        TimeSpan r1 = {|TF8009:DateTime.Now - started|};
        double r2 = ({|TF8009:DateTime.Now - started|}).TotalSeconds;
        TimeSpan r3 = {|TF8009:DateTime.Now.Subtract|}(started);
        TimeSpan r4 = DateTime.UtcNow - started;
        TimeSpan r5 = started - DateTime.Now;
        DateTime r6 = DateTime.Now - TimeSpan.FromSeconds(1);
        string r7 = DateTime.Now.ToString();
    }
}");
        }
    }
}
