using NUnit.Framework;
using TarragonFly.Analyzers.Rules.StaticState;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.StaticState
{
    // TF7000 visible static fields, TF7001 mutable public static collections, TF7003 statics in generic types.
    public class StaticStateTests
    {
        [Test]
        public void NonPrivateStaticField_IsReported()
        {
            AnalyzerVerifier.Verify(new VisibleStaticFieldAnalyzer(), @"
using System;
using System.Collections.Generic;

public class GameState
{
    public static int {|TF7000:Score|};
    protected static int {|TF7000:Lives|};
    internal static int {|TF7000:Level|};
    public static int {|TF7000:First|}, {|TF7000:Second|};
    public static List<int> {|TF7000:Items|} = new List<int>();
    public static readonly int Max = 3;
    public const int Min = 1;
    private static int hidden;
    public static int Property { get; set; }
    public static event Action Changed;

    class Nested
    {
        public static int {|TF7000:Inside|};
    }
}");
        }

        [Test]
        public void NonPrivateStaticField_InTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new VisibleStaticFieldAnalyzer(), "public class Fixture { public static int Count; }", SnippetAssembly.Tests());
        }

        [Test]
        public void MutablePublicStaticCollection_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new MutablePublicStaticCollectionAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

public class Registry
{
    public static readonly {|TF7001:List<int>|} Items = new List<int>();
    public static readonly {|TF7001:Dictionary<string, int>|} Lookup = new Dictionary<string, int>();
    public static readonly {|TF7001:IList<int>|} AsInterface = new List<int>();
    public static readonly {|TF7001:Stack<int>|} Undo = new Stack<int>();
    public static {|TF7001:int[]|} Numbers = { 1 };

    public static readonly int[] ReadOnlyNumbers = { 1 };
    public static readonly IReadOnlyList<int> View = new List<int>();
    public static readonly ReadOnlyCollection<int> Wrapper = new ReadOnlyCollection<int>(new List<int>());
    public static readonly ImmutableArray<int> Immutable = ImmutableArray<int>.Empty;
    public static readonly IList<int> WrappedList = Array.AsReadOnly(new[] { 1 });
    protected static readonly List<int> Protected = new List<int>();
    private static readonly List<int> hidden = new List<int>();
}

internal class Internal
{
    public static readonly List<int> NotVisible = new List<int>();
}");
        }

        [Test]
        public void ReadonlyCollectionFilledByCode_IsReported()
        {
            AnalyzerVerifier.Verify(new MutablePublicStaticCollectionAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

public static class Source
{
    public static List<int> Values => new List<int>();
}

public class Registry
{
    public static readonly {|TF7001:List<int>|} Created = new List<int>();
    public static readonly {|TF7001:List<int>|} TargetTyped = new();
    public static readonly {|TF7001:List<int>|} Called = Enumerable.Range(0, 3).ToList();
    public static readonly {|TF7001:IList<int>|} AsInterface = new List<int>();
    public static readonly {|TF7001:SortedSet<int>|} Sorted = new SortedSet<int>();
    public static readonly {|TF7001:List<int>|} Filled;
    public static readonly {|TF7001:string[]|} Names;
    public static readonly IList<int> Literal = new[] { 1 };
    public static readonly List<int> Nothing;
    public static readonly List<int> Empty = null;
    public static readonly List<int> FromProperty = Source.Values;
    public static readonly List<int> Cleared;
    public static readonly string[] Unset;
    public static readonly {|TF7001:Queue<int>|} Waiting = new Queue<int>();
    public static readonly {|TF7001:Stack<int>|} Undo = new Stack<int>();
    public static readonly {|TF7001:System.Collections.ArrayList|} Loose = new System.Collections.ArrayList();
    public static readonly Queue<int> Unfilled;
    public static {|TF7001:Queue<int>|} Writable = new Queue<int>();

    static Registry()
    {
        Filled = new List<int>();
        Names = Source.Values.Select(value => value.ToString()).ToArray();
        Cleared = null;
        Unset = null;
    }
}");
        }

        [Test]
        public void ReadonlyArrayFilledByCode_IsReported_AndNestedProtectedTypesCount()
        {
            AnalyzerVerifier.Verify(new MutablePublicStaticCollectionAnalyzer(), @"
using System;
using System.Linq;

public enum Mode { One, Two }

public static class Options
{
    public static readonly {|TF7001:string[]|} Names = Enum.GetNames(typeof(Mode));
    public static readonly {|TF7001:int[]|} Counts = Enumerable.Range(0, 3).ToArray();
    public static readonly {|TF7001:string[]|} Filled;
    public static readonly {|TF7001:string[]|} First = { ""a"" }, Second = Enum.GetNames(typeof(Mode));
    public static readonly float[] Scales = { 0.75f, 1f, 1.25f };
    public static readonly string[] Labels = new[] { ""75%"", ""100%"" };
    public static readonly string[] Sized = new string[3];
    public static readonly string[] Cast = (string[])Make();

    static Options() { Filled = new[] { ""a"" }; }

    static object Make() => new[] { ""a"" };
}

public class ShaderGui
{
    protected class Styles
    {
        public static readonly {|TF7001:string[]|} Names = Enum.GetNames(typeof(Mode));
        public static {|TF7001:int[]|} Counts = { 1 };
        public static readonly int[] Table = { 1 };
        protected static int[] Hidden = { 1 };
    }

    private class Private
    {
        public static readonly string[] Names = Enum.GetNames(typeof(Mode));
    }
}");
        }

        [Test]
        public void StaticFieldNotUsingTheTypeParameters_IsReported()
        {
            AnalyzerVerifier.Verify(new StaticFieldInGenericTypeAnalyzer(), @"
using System;
using System.Collections.Generic;

class Pool<T>
{
    static int {|TF7003:count|};
    static int[] {|TF7003:buckets|};
    static readonly List<T> items = new List<T>();
    static T last;
    static readonly Func<T, int> converter = null;
    const int Max = 10;

    class Inner
    {
        static int {|TF7003:nested|};
    }
}

class Plain
{
    static int count;
}");
        }

        [Test]
        public void TypeDependentInitializersAndAutoProperties_AreChecked()
        {
            AnalyzerVerifier.Verify(new StaticFieldInGenericTypeAnalyzer(), @"
using System;

static class Ids
{
    public static int For<T>() => 0;
}

class Pool<T>
{
    static readonly int safetyId = Ids.For<Pool<T>>();
    static readonly string name = typeof(T).Name;
    static readonly int {|TF7003:plain|} = Ids.For<int>();
    public static int {|TF7003:Count|} { get; set; }
    public static int {|TF7003:Fixed|} { get; } = 4;
    public static Type Element { get; } = typeof(T);
    public static int Computed => 0;

    public struct ReadOnly
    {
        static readonly int safetyId = Ids.For<ReadOnly>();
        static readonly int {|TF7003:shared|} = Ids.For<string>();
    }
}");
        }
    }
}