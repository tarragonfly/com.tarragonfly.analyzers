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
    }
}