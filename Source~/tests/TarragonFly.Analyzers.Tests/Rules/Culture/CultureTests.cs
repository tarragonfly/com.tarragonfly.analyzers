using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Culture;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Culture
{
    // TF3500 culture-sensitive text, TF3501 date parsed without a culture.
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
    }
}