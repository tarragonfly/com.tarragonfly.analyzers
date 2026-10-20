using NUnit.Framework;
using TarragonFly.Analyzers.Rules.LikelyBugs;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.LikelyBugs
{
    // TF2100 ignored result of a TryParse call.
    public class IgnoredTryParseResultTests
    {
        [Test]
        public void TryParseUsedAsAStatement_IsReported()
        {
            AnalyzerVerifier.Verify(new IgnoredTryParseResultAnalyzer(), @"
using System;
using System.Collections.Generic;
using System.Globalization;

class Settings
{
    readonly Dictionary<string, int> values = new Dictionary<string, int>();

    float Volume(string text)
    {
        int.{|TF2100:TryParse|}(text, out int volume);
        float.{|TF2100:TryParse|}(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale);
        DateTime.{|TF2100:TryParseExact|}(text, ""yyyy"", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime year);
        _ = int.TryParse(text, out int ignored);
        if (int.TryParse(text, out int checkedVolume)) return checkedVolume;
        bool ok = float.TryParse(text, out float parsed);

        values.TryGetValue(""music"", out int music);
        TryParseLevel(text, out int level);
        {|TF2100:TryParse|}(text, out int own);
        return volume * scale + music + level;
    }

    static bool TryParse(string text, out int value)
    {
        value = 0;
        return false;
    }

    static bool TryParseLevel(string text, out int level)
    {
        level = 0;
        return false;
    }
}");
        }
    }
}