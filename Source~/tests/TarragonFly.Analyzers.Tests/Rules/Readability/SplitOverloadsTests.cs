using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Readability;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Readability
{
    public class SplitOverloadsTests
    {
        [Test]
        public void SplitOverloads_AreReportedOnceOnTheFirst()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
class AudioPlayer
{
    public void {|TF1023:Play|}(int clip) { }

    public void Stop() { }

    public void Play(string clip) { }

    public void Play(string clip, float volume) { }

    public {|TF1023:AudioPlayer|}() { }

    private int volume;

    public AudioPlayer(int volume) { this.volume = volume; }

    public interface ISound
    {
        void {|TF1023:Hit|}();

        void Miss();

        void Hit(int strength);
    }
}");
        }

        [Test]
        public void AdjacentOverloads_AndDifferentAccessOrStatic_AreFine()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
class AudioPlayer
{
    public void Fade(int a) { }

    public void Fade(float a) { }

    void Load(int a) { }

    public int Volume { get; set; }

    private static void Load(string a) { }

    public void Mix(int a) { }

    public void Unrelated() { }

    private void Mix(string a) { }

    public static AudioPlayer operator +(AudioPlayer a, int b) => a;

    public void Between() { }

    public static AudioPlayer operator +(AudioPlayer a, string b) => a;
}");
        }

        [Test]
        public void OverloadsSplitAcrossPartialParts_AreReported()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
partial class AudioPlayer
{
    public void {|TF1023:Jump|}(int a) { }
}

partial class AudioPlayer
{
    public void Jump(string a) { }
}");
        }
    }
}