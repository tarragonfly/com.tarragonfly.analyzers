using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2068-TF2071: names that change meaning in C# 14.
    public class NewKeywordNameTests
    {
        [Test]
        public void Field_InsideAPropertyAccessor_IsReported()
        {
            AnalyzerVerifier.Verify(new FieldKeywordNameAnalyzer(), @"
using System.Linq;

class Player
{
    private int field;
    private int[] items = new int[3];

    public int Health
    {
        get { return {|TF2068:field|}; }
        set { {|TF2068:field|} = value; }
    }

    public int Doubled => {|TF2068:field|} * 2;

    public string Name => nameof({|TF2068:field|});

    public int Local
    {
        get
        {
            int {|TF2068:field|} = 1;
            return field;
        }
    }

    public int Sum => items.Select({|TF2068:field|} => field + 1).Sum();
}");
        }

        [Test]
        public void Field_OutsidePropertiesOrQualified_IsFine()
        {
            AnalyzerVerifier.Verify(new FieldKeywordNameAnalyzer(), @"
class Player
{
    private int field;

    public int Qualified => this.field;
    public int Escaped => @field;
    public int this[int field] => field;
    public event System.Action Changed { add { field = 1; } remove { field = 2; } }

    int Method() => field;
}");
        }

        [Test]
        public void Field_AsAQueryRangeVariable_IsReported_ButNotThroughConditionalAccess()
        {
            AnalyzerVerifier.Verify(new FieldKeywordNameAnalyzer(), @"
using System.Linq;

class Holder { public int field; }

class Queries
{
    private int[] items = { 1, 2, 3 };
    private Holder holder = new Holder();

    public int From => (from {|TF2068:field|} in items select field).Sum();
    public int Let => (from x in items let {|TF2068:field|} = x select field).Sum();
    public int Into => (from x in items group x by x into {|TF2068:field|} select field.Key).Sum();
    public int Conditional => holder?.field ?? 0;
}");
        }

        [Test]
        public void Extension_AsAConstructorOrAliasName_IsReported()
        {
            AnalyzerVerifier.Verify(new ExtensionKeywordNameAnalyzer(), @"
using {|TF2069:extension|} = System.String;

namespace Game
{
    class @extension
    {
        public {|TF2069:extension|}() { }
        {|TF2069:extension|}(int value) { }
    }
}");
        }

        [Test]
        public void Extension_AsATypeOrMemberTypeName_IsReported()
        {
            AnalyzerVerifier.Verify(new ExtensionKeywordNameAnalyzer(), @"
using System.Collections.Generic;

class {|TF2069:extension|} { }

class Uses<{|TF2069:extension|}>
{
}

class Members
{
    private {|TF2069:extension|} field;
    public {|TF2069:extension|}[] Items;
    public {|TF2069:extension|} Property { get; set; }
    public {|TF2069:extension|} Make(extension parameter)
    {
        extension local = parameter;
        List<extension> list = null;
        return local ?? field;
    }
}");
        }

        [Test]
        public void Extension_InTestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new ExtensionKeywordNameAnalyzer(), "class extension { }", SnippetAssembly.Tests());
        }

        [Test]
        public void Partial_AsAReturnType_IsReported()
        {
            AnalyzerVerifier.Verify(new PartialReturnTypeAnalyzer(), @"
class partial { }

class Uses
{
    public {|TF2070:partial|} Make() => null;
    public static {|TF2070:partial|} MakeStatic() => null;
    public @partial Escaped() => null;
    public partial[] Many() => null;
    private partial field;
    public partial Property => field;

    void Local()
    {
        {|TF2070:partial|} Inner() => null;
        partial value = Inner();
    }
}");
        }

        [Test]
        public void Partial_QualifiedAfterRefOrInADelegate_IsReported_ButGenericMethodsAreFine()
        {
            AnalyzerVerifier.Verify(new PartialReturnTypeAnalyzer(), @"
namespace Game
{
    public class partial { }

    public delegate {|TF2070:partial|} Factory();

    public class Uses
    {
        private partial stored;

        public Game.{|TF2070:partial|} Qualified() => null;
        public ref {|TF2070:partial|} Get() => ref stored;
        public partial Make<T>() => null;

        void Run()
        {
            partial Local<T>() => null;
            Local<int>();
        }
    }
}");
        }

        [Test]
        public void Scoped_AsAnArrayOrGenericLambdaParameterType_IsReported()
        {
            AnalyzerVerifier.Verify(new ScopedLambdaParameterAnalyzer(), @"
class scoped { }
class scoped<T> { }

delegate int Many(scoped[] values);
delegate int Generic(scoped<int> value);

class Uses
{
    public Many ByArray = ({|TF2071:scoped|}[] values) => 1;
    public Generic ByGeneric = ({|TF2071:scoped|}<int> value) => 2;
}");
        }

        // "(scoped) => scoped + 1" is reported too (checked with Roslyn 4.8), but Roslyn 4.3, which these
        // tests compile with, already reads that "scoped" as a modifier.
        [Test]
        public void Scoped_InLambdaParameters_IsReported()
        {
            AnalyzerVerifier.Verify(new ScopedLambdaParameterAnalyzer(), @"
using System;

class scoped { }

delegate int Handler(scoped value);

class Uses
{
    void Run()
    {
        Handler typed = ({|TF2071:scoped|} value) => 1;
        Handler escaped = (@scoped value) => 2;
        Func<int, int> simple = scoped => scoped + 1;
        Handler anonymous = delegate (scoped value) { return 3; };
    }

    void Method(scoped value) { }
}");
        }
    }
}