using System.Linq;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Logging;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Logging
{
    // TF3100 log message without a tag.
    public class LogWithoutPrefixTests
    {
        private const string Code = @"
using UnityEngine;

class AudioManager : MonoBehaviour
{
    const string Prefix = ""[AudioManager]"";

    void Awake()
    {
        Debug.LogWarning({|TF3100:""Clip registry is not assigned.""|});
        Debug.LogError({|TF3100:$""Missing {name}""|}, this);
        Debug.LogFormat({|TF3100:""Volume {0}""|}, 1f);
        Debug.Log({|TF3100:""""|});
        Debug.Log({|TF3100:""[Audio Mixer] tags have no spaces""|});
        Debug.Log({|TF3100:""[] empty tag""|});
        Debug.Log({|TF3100:""Audio] no opening bracket""|});

        Debug.LogWarning(""[AudioManager] Clip registry is not assigned."");
        Debug.LogError($""[AudioManager] Missing {name}"", this);
        Debug.LogError($""[{nameof(AudioManager)}] Missing {name}"");
        Debug.Log(Prefix + "" ready"");
        Debug.LogFormat(this, ""[AudioManager] Volume {0}"", 1f);
        Debug.Log(""[Audio] "" + name);
        Debug.Log(""[ScenarioEngine] subsystem tags are fine"");
        Debug.Log(""["" + name + ""] partly known"");
        Debug.Log(""[Audio"" + name);
        Debug.Log(name);
        Debug.LogException(new System.Exception(""no tag needed""));
    }

    class Channel
    {
        void Play()
        {
            Debug.Log({|TF3100:""nested type, so the suggestion is [Channel]""|});
            Debug.Log(""[Mixer] any tag"");
        }
    }
}

class Logger
{
    void Log(string message) { }
    void Write() => Log(""not Unity's Debug"");
}";

        [Test]
        public void MessageWithoutATag_IsReported()
        {
            AnalyzerVerifier.Verify(new LogWithoutPrefixAnalyzer(), Code);
        }

        [Test]
        public void Suggestion_UsesTheInnermostTypeName()
        {
            string[] messages = AnalyzerVerifier.Run(new LogWithoutPrefixAnalyzer(), Markup.Without(Code, "TF3100"))
                .Select(d => d.GetMessage()).ToArray();
            Assert.That(messages, Has.Some.Contains("'[Channel]'"));
            Assert.That(messages, Has.Some.Contains("'[AudioManager]'"));
        }

        [Test]
        public void TestCode_IsNotChecked()
        {
            AnalyzerVerifier.Verify(new LogWithoutPrefixAnalyzer(), Markup.Without(Code, "TF3100"), SnippetAssembly.Tests());
        }

        [Test]
        public void TagShape_ComesFromSettings()
        {
            AnalyzerVerifier.Verify(new LogWithoutPrefixAnalyzer(), @"
using UnityEngine;

class Spawner
{
    void Spawn()
    {
        Debug.Log(""Spawner: ready"");
        Debug.Log(""Waves: any tag of that shape"");
        Debug.Log({|TF3100:""[Spawner] ready""|});
    }
}", SnippetAssembly.WithSettings("log_prefix = {Type}:"));
        }

        [Test]
        public void PrefixWithoutTypePlaceholder_MustMatchExactly()
        {
            AnalyzerVerifier.Verify(new LogWithoutPrefixAnalyzer(), @"
using UnityEngine;

class Spawner
{
    void Spawn()
    {
        Debug.Log(""GAME: ready"");
        Debug.Log({|TF3100:""[Spawner] ready""|});
    }
}", SnippetAssembly.WithSettings("log_prefix = GAME:"));
        }
    }
}