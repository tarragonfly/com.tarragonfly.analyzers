using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Performance;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Performance
{
    // TF8000 Count() for any items, TF8004 shorter LINQ chain, TF8010 FirstOrDefault instead of Find,
    // TF8011 All instead of TrueForAll, TF8012 Any instead of Exists, TF8013 OrderBy before Where,
    // TF8014 First instead of an index, TF8015 LINQ Min/Max on a SortedSet, TF8016 one-character
    // string prefix, TF8017 LINQ First on a LinkedList, TF8018 Any instead of Contains.
    public class LinqCallTests
    {
        [Test]
        public void CountComparedForAnyItems_IsReported()
        {
            AnalyzerVerifier.Verify(new CountInsteadOfAnyAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(IEnumerable<int> enemies, List<int> list)
    {
        bool r1 = enemies.{|TF8000:Count|}() > 0;
        bool r2 = enemies.{|TF8000:Count|}() == 0;
        bool r3 = enemies.{|TF8000:Count|}(e => e > 1) >= 1;
        bool r4 = 0 < enemies.{|TF8000:Count|}();
        bool r5 = enemies.{|TF8000:Count|}() < 1;
        bool r6 = list.Count > 0;
        bool r7 = enemies.Count() > 1;
        bool r8 = enemies.Count() >= 0;
        bool r9 = enemies?.{|TF8000:Count|}() > 0;
        bool r10 = 0 == enemies?.{|TF8000:Count|}();
    }
}");
        }

        [Test]
        public void LinqChainThroughConditionalAccess_IsReportedFromTheDot()
        {
            AnalyzerVerifier.Verify(new ShorterLinqChainAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Squad : List<int> { }

class Target
{
    void Check(List<int> maybe, Squad squad)
    {
        bool? r1 = maybe?{|TF8004:.Where|}(x => x > 1).Any();
        int? r2 = maybe?{|TF8004:.ToList|}().First();
        int? r3 = maybe?.Count();
        int r4 = squad.{|TF8004:Count|}();
    }
}");
        }

        [Test]
        public void LinqChainWithAnUnneededStep_IsReported()
        {
            AnalyzerVerifier.Verify(new ShorterLinqChainAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, IEnumerable<int> seq, IEnumerable<object> objects, IList<int> slots, int[] arr)
    {
        int r1 = list.{|TF8004:Where|}(x => x > 1).First();
        bool r2 = list.{|TF8004:Where|}(x => x > 1).Any();
        IEnumerable<string> r3 = objects.{|TF8004:Where|}(o => o is string).Select(o => (string)o);
        int r4 = list.{|TF8004:Count|}();
        int r5 = slots.{|TF8004:Count|}();
        int r6 = seq.{|TF8004:ToList|}().First();
        int[] r7 = seq.{|TF8004:ToList|}().ToArray();
        int r8 = arr.Count();
        int r9 = list.First(x => x > 1);
        bool r10 = list.Where(x => x > 1).Any(x => x < 5);
        IEnumerable<int> r11 = list.Where(x => x > 1).Where(x => x < 5);
        list.ToList().ForEach(x => System.Console.WriteLine(x));
    }
}");
        }

        [Test]
        public void CopiesOfNonCollectionsAndIndexedWhere_AreFine_AndAsCastsCount()
        {
            AnalyzerVerifier.Verify(new ShorterLinqChainAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.Linq;

struct QueryBuilder
{
    public List<string> ToList() => new List<string>();
}

class Target
{
    void Check(List<string> list, List<object> objects, QueryBuilder builder, Type[] types)
    {
        Span<Type> span = types;
        string r1 = builder.ToList().Concat(list).First();
        IEnumerable<string> r2 = span.ToArray().Select(t => t.Name);
        string r3 = list.Where((name, index) => index > 1).FirstOrDefault();
        IEnumerable<string> r4 = objects.{|TF8004:Where|}(o => o is string).Select(o => o as string);
    }
}");
        }

        [Test]
        public void ListMethodsOnDerivedListsAndThroughConditionalAccess_AreReported()
        {
            const string Code = @"
using System.Collections.Generic;
using System.Linq;

class Squad : List<int> { }

class Target
{
    bool? Check(List<int> maybe, Squad squad)
    {
        return maybe?.{|ID:CALL|}(x => x > 1) != null && squad.{|ID:CALL|}(x => x > 1) != null;
    }
}";
            AnalyzerVerifier.Verify(new FirstOrDefaultInsteadOfFindAnalyzer(), Code.Replace("ID", "TF8010").Replace("CALL", "FirstOrDefault").Replace(" != null", " > 0"));
            AnalyzerVerifier.Verify(new AllInsteadOfTrueForAllAnalyzer(), Code.Replace("ID", "TF8011").Replace("CALL", "All").Replace(" != null", " == true"));
            AnalyzerVerifier.Verify(new AnyInsteadOfExistsAnalyzer(), Code.Replace("ID", "TF8012").Replace("CALL", "Any").Replace(" != null", " == true"));
        }

        [Test]
        public void ListOrArrayLinqWithAListMethod_IsReported()
        {
            AnalyzerVerifier.Verify(new FirstOrDefaultInsteadOfFindAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, int[] arr, IEnumerable<int> seq)
    {
        int r1 = list.{|TF8010:FirstOrDefault|}(x => x > 1);
        int r2 = arr.{|TF8010:FirstOrDefault|}(x => x > 1);
        int r3 = seq.FirstOrDefault(x => x > 1);
        int r4 = list.FirstOrDefault();
    }
}");
            AnalyzerVerifier.Verify(new AllInsteadOfTrueForAllAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, int[] arr, IEnumerable<int> seq)
    {
        bool r1 = list.{|TF8011:All|}(x => x > 1);
        bool r2 = arr.{|TF8011:All|}(x => x > 1);
        bool r3 = seq.All(x => x > 1);
    }
}");
        }

        [Test]
        public void AnyWithConditionOnListOrArray_IsReported_ExceptEqualityChecks()
        {
            AnalyzerVerifier.Verify(new AnyInsteadOfExistsAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, int[] arr, IEnumerable<int> seq)
    {
        bool r1 = list.{|TF8012:Any|}(x => x > 1);
        bool r2 = arr.{|TF8012:Any|}(x => x > 1);
        bool r3 = arr.{|TF8012:Any|}(x => x == 3);
        bool r4 = list.Any(x => x == 3);
        bool r5 = list.Any();
        bool r6 = seq.Any(x => x > 1);
    }
}");
        }

        [Test]
        public void WhereAfterOrderBy_IsReported()
        {
            AnalyzerVerifier.Verify(new OrderByBeforeWhereAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list)
    {
        IEnumerable<int> r1 = list.OrderBy(x => x).{|TF8013:Where|}(x => x > 1);
        IEnumerable<int> r2 = list.OrderByDescending(x => x).{|TF8013:Where|}(x => x > 1);
        IEnumerable<int> r3 = list.Where(x => x > 1).OrderBy(x => x);
        IEnumerable<int> r4 = list.OrderBy(x => x).ThenBy(x => -x).Where(x => x > 1);
    }
}");
        }

        [Test]
        public void WhereAfterOrderByThroughParenthesesOrConditionalAccess_IsReported_ExceptIndexedWhere()
        {
            AnalyzerVerifier.Verify(new OrderByBeforeWhereAnalyzer(), @"
#nullable enable
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, List<int>? maybe)
    {
        IEnumerable<int> r1 = (list.OrderBy(x => x)).{|TF8013:Where|}(x => x > 1);
        IEnumerable<int>? r2 = maybe?.OrderBy(x => x).{|TF8013:Where|}(x => x > 1);
        IEnumerable<int> r3 = list.OrderBy(x => x)!.{|TF8013:Where|}(x => x > 1);
        IEnumerable<int>? r4 = maybe?.OrderBy(x => x)?.{|TF8013:Where|}(x => x > 1);
        IEnumerable<int> r5 = list.OrderBy(x => x).Where((x, i) => i > 1);
        IEnumerable<int> r6 = ((IEnumerable<int>)list.OrderBy(x => x)).Where(x => x > 1);
    }
}");
        }

        [Test]
        public void FirstOrLastOnIndexableList_IsReported()
        {
            AnalyzerVerifier.Verify(new FirstInsteadOfIndexAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, int[] arr, IReadOnlyList<int> ro, IEnumerable<int> seq, string s)
    {
        int r1 = list.{|TF8014:First|}();
        int r2 = arr.{|TF8014:Last|}();
        int r3 = ro.{|TF8014:ElementAt|}(1);
        int r4 = seq.First();
        int r5 = list.First(x => x > 1);
        char r6 = s.First();
        int r7 = list.FirstOrDefault();
    }
}");
        }

        [Test]
        public void LinqOnSortedSetAndLinkedListEnds_IsReported()
        {
            AnalyzerVerifier.Verify(new LinqMinMaxOnSortedSetAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(SortedSet<int> set, HashSet<int> hash)
    {
        int r1 = set.{|TF8015:Min|}();
        int r2 = set.{|TF8015:Max|}();
        int r3 = set.Min;
        int r4 = set.Min(x => x);
        int r5 = hash.Max();
    }
}");
            AnalyzerVerifier.Verify(new LinqFirstOnLinkedListAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(LinkedList<int> path)
    {
        int r1 = path.{|TF8017:First|}();
        int r2 = path.{|TF8017:Last|}();
        LinkedListNode<int> r3 = path.Last;
        int r4 = path.First(x => x > 1);
        int r5 = path.LastOrDefault();
    }
}");
        }

        [Test]
        public void DerivedSortedSetsAndLinkedListsAndConditionalAccess_AreReported()
        {
            AnalyzerVerifier.Verify(new LinqMinMaxOnSortedSetAnalyzer(), @"
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

class Scores : SortedSet<int>
{
    public int Best() => this.{|TF8015:Max|}();
}

class Target
{
    int? Check(Scores scores, SortedSet<int> maybe, ImmutableSortedSet<int> frozen)
    {
        return scores.{|TF8015:Min|}() + maybe?.{|TF8015:Max|}() + frozen.{|TF8015:Max|}();
    }
}");
            AnalyzerVerifier.Verify(new LinqFirstOnLinkedListAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Route : LinkedList<int>
{
    public int End() => this.{|TF8017:Last|}();
}

class Target
{
    int? Check(Route route, LinkedList<int> maybe)
    {
        return route.{|TF8017:First|}() + maybe?.{|TF8017:Last|}();
    }
}");
        }

        [Test]
        public void OneCharacterStringPrefix_IsReported()
        {
            AnalyzerVerifier.Verify(new SingleCharStringPrefixAnalyzer(), @"
using System;

class Target
{
    const string Slash = ""/"";

    void Check(string path)
    {
        bool r1 = path.{|TF8016:StartsWith|}(""a"");
        bool r2 = path.{|TF8016:EndsWith|}(""\n"");
        bool r3 = path.StartsWith(""ab"");
        bool r4 = path.StartsWith('a');
        bool r5 = path.StartsWith(""a"", StringComparison.Ordinal);
        bool r6 = path.EndsWith(Slash);
        bool r7 = path.StartsWith("""");
    }
}");
        }

        [Test]
        public void AnyEqualityOnCollectionWithContains_IsReported()
        {
            AnalyzerVerifier.Verify(new AnyInsteadOfContainsAnalyzer(), @"
using System.Collections.Generic;
using System.Linq;

class Target
{
    void Check(List<int> list, HashSet<string> set, int[] arr, IEnumerable<int> seq, int wanted)
    {
        bool r1 = list.{|TF8018:Any|}(x => x == 3);
        bool r2 = list.{|TF8018:Any|}(x => wanted == x);
        bool r3 = list.{|TF8018:Any|}(x => x.Equals(wanted));
        bool r4 = set.{|TF8018:Any|}(s => s == ""boss"");
        bool r5 = arr.Any(x => x == 3);
        bool r6 = seq.Any(x => x == 3);
        bool r7 = list.Any(x => x > 3);
        bool r8 = list.Any(x => x == x + 1);
    }
}");
        }
    }
}