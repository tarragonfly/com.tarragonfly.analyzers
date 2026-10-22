using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Culture;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Culture
{
    // TF3500 culture-sensitive text, TF3501 date parsed without a culture, TF3102 number parsed without a
    // culture, TF3103 number or date turned into text without a culture, TF3104 prefix check with the
    // player's culture, TF3105 character case changed with the player's culture.
    public class CultureTests
    {
        [Test]
        public void TextOperationUsingThePlayersCulture_IsReported()
        {
            const string Code = @"
using System;
using System.Globalization;

class Target
{
    void Check(string id, string other, float speed, double d, decimal m, DateTime date, int n, long l)
    {
        string r1 = id.{|TF3500:ToLower|}();
        string r2 = id?.{|TF3500:ToUpper|}();
        int r3 = id.{|TF3500:IndexOf|}(""boss"");
        int r4 = id.{|TF3500:LastIndexOf|}(""boss"", 2);
        int r5 = string.{|TF3500:Compare|}(id, other, true);
        int r6 = id.{|TF3500:CompareTo|}(other);
        string r7 = speed.{|TF3500:ToString|}();
        string r8 = d.{|TF3500:ToString|}();
        string r9 = m.{|TF3500:ToString|}();
        string r10 = date.{|TF3500:ToString|}();
        string f1 = id.ToLowerInvariant();
        string f2 = id.ToUpper(CultureInfo.InvariantCulture);
        int f3 = id.IndexOf('b');
        int f4 = id.IndexOf(""boss"", StringComparison.Ordinal);
        int f5 = string.Compare(id, other, StringComparison.Ordinal);
        string f6 = n.ToString();
        string f7 = l.ToString();
        string f8 = speed.ToString(""F2"");
        string f9 = d.ToString(CultureInfo.InvariantCulture);
        bool f10 = id.StartsWith(""b"");
        char f11 = char.ToUpper('b');
    }
}";
            AnalyzerVerifier.Verify(new CultureSensitiveTextAnalyzer(), Code);
            AnalyzerVerifier.Verify(new CultureSensitiveTextAnalyzer(), Code.Replace("{|TF3500:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void DateParsedWithoutACulture_IsReported()
        {
            AnalyzerVerifier.Verify(new DateParsedWithoutCultureAnalyzer(), @"
using System;
using System.Globalization;

class Target
{
    void Check(string text)
    {
        DateTime r1 = {|TF3501:DateTime.Parse(text)|};
        DateTime r2 = {|TF3501:DateTime.Parse(text, null)|};
        bool r3 = {|TF3501:DateTime.TryParse(text, out DateTime parsed)|};
        DateTime r4 = {|TF3501:DateTime.ParseExact(text, ""yyyy"", null)|};
        TimeSpan r5 = {|TF3501:TimeSpan.Parse(text)|};
        DateTimeOffset r6 = {|TF3501:DateTimeOffset.Parse(text)|};
        DateTime r7 = {|TF3501:DateTime.Parse(text, provider: null)|};
        DateTime f1 = DateTime.Parse(text, CultureInfo.InvariantCulture);
        DateTime f2 = DateTime.ParseExact(text, ""yyyy"", CultureInfo.InvariantCulture);
        bool f3 = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime other);
        int f4 = int.Parse(text);
    }
}");
        }

        [Test]
        public void NumberParsedWithoutACulture_IsReported()
        {
            AnalyzerVerifier.Verify(new NumberParsedWithoutCultureAnalyzer(), @"
using System;
using System.Globalization;

class Target
{
    void Check(string text, ReadOnlySpan<char> span)
    {
        float r1 = {|TF3102:float.Parse(text)|};
        bool r2 = {|TF3102:double.TryParse(text, out double parsed)|};
        decimal r3 = {|TF3102:decimal.Parse(text, NumberStyles.Number)|};
        float r4 = {|TF3102:float.Parse(text, null)|};
        float r5 = {|TF3102:float.Parse(span)|};
        double r6 = {|TF3102:Convert.ToDouble(text)|};
        float r7 = {|TF3102:Single.Parse(text)|};
        decimal r8 = {|TF3102:Convert.ToDecimal(text, provider: null)|};
        float f1 = float.Parse(text, CultureInfo.InvariantCulture);
        bool f2 = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double other);
        int f3 = int.Parse(text);
        double f4 = Convert.ToDouble(text, CultureInfo.InvariantCulture);
        double f5 = Convert.ToDouble(3);
        long f6 = long.Parse(text);
    }
}");
        }

        [Test]
        public void NumberOrDateWrittenWithoutACulture_IsReported()
        {
            const string Code = @"
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

class Target
{
    void Check(float speed, double d, decimal m, DateTime date, DateTimeOffset offset, float? maybe, int count, StringBuilder builder)
    {
        string r1 = speed.{|TF3103:ToString|}(""F2"");
        string r2 = d.{|TF3103:ToString|}(""0.00"", null);
        string r3 = m.{|TF3103:ToString|}(""N"");
        string r4 = date.{|TF3103:ToString|}(""yyyy-MM-dd"");
        string r5 = offset.{|TF3103:ToString|}(""yyyy"");
        string r6 = maybe?.{|TF3103:ToString|}(""F1"");
        string r7 = Convert.{|TF3103:ToString|}(speed);
        string r8 = string.{|TF3103:Format|}(""{0}"", speed);
        string r9 = string.{|TF3103:Format|}(""{0} {1} {2} {3}"", count, count, count, date);
        builder.{|TF3103:AppendFormat|}(""{0}"", m);
        string f1 = speed.ToString(""F2"", CultureInfo.InvariantCulture);
        string f2 = speed.ToString();
        string f3 = $""{speed}"";
        Debug.LogFormat(""{0}"", speed);
        string f4 = string.Format(""{0}"", count);
        string f5 = string.Format(CultureInfo.InvariantCulture, ""{0}"", speed);
        string f6 = Convert.ToString(count);
        string f7 = count.ToString(""N0"");
        string f8 = Convert.ToString(speed, CultureInfo.CurrentCulture);
        builder.AppendFormat(CultureInfo.InvariantCulture, ""{0}"", m);
    }
}";
            AnalyzerVerifier.Verify(new NumberOrDateFormattedWithoutCultureAnalyzer(), Code);
            AnalyzerVerifier.Verify(new NumberOrDateFormattedWithoutCultureAnalyzer(), Code.Replace("{|TF3103:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void PrefixOrSuffixCheckUsingThePlayersCulture_IsReported()
        {
            const string Code = @"
using System;
using System.Globalization;

class Target
{
    void Check(string id, string other)
    {
        bool r1 = id.{|TF3104:StartsWith|}(""boss"");
        bool r2 = id?.{|TF3104:EndsWith|}(other) == true;
        bool f1 = id.StartsWith(""boss"", StringComparison.Ordinal);
        bool f2 = id.EndsWith('x');
        bool f3 = id.StartsWith(""boss"", true, CultureInfo.InvariantCulture);
        bool f4 = id.Contains(""boss"");
    }
}";
            AnalyzerVerifier.Verify(new PrefixOrSuffixCheckWithCultureAnalyzer(), Code);
            AnalyzerVerifier.Verify(new PrefixOrSuffixCheckWithCultureAnalyzer(), Code.Replace("{|TF3104:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }

        [Test]
        public void CharacterCaseChangedWithThePlayersCulture_IsReported()
        {
            const string Code = @"
using System.Globalization;

class Target
{
    void Check(char c, string id)
    {
        char r1 = char.{|TF3105:ToLower|}(c);
        char r2 = char.{|TF3105:ToUpper|}(id[0]);
        char f1 = char.ToLowerInvariant(c);
        char f2 = char.ToUpper(c, CultureInfo.InvariantCulture);
        string f3 = id.ToLowerInvariant();
    }
}";
            AnalyzerVerifier.Verify(new CharCaseWithCultureAnalyzer(), Code);
            AnalyzerVerifier.Verify(new CharCaseWithCultureAnalyzer(), Code.Replace("{|TF3105:", string.Empty).Replace("|}", string.Empty), SnippetAssembly.Tests());
        }
    }
}