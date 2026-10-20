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
        public void OverloadsApartForAccessAbstractOrInterfaceSections_AreFine()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
using System;

interface I1 { void R(int a); void U(int a); }
interface I2 { void R(string a); void U(string a); }
interface IPlayer { void Play(int clip); }

static class Selection
{
    public static void Remove(int item) { }
    static void Remove(object item) { }
    public static void Remove(bool item) { }
}

class Sections : I1, I2
{
    public void R(int a) { }
    public void U(int a) { }
    public void R(string a) { }
    public void U(string a) { }
}

class Cards : I1, IDisposable
{
    public void R(string a) { }
    public void Dispose() { }
    public void R(int a) { }
    public void U(int a) { }
}

abstract class Provider
{
    public void A(int c) { }
    public void B() { }
    public abstract void A(string c);
    public abstract void A(bool c);
}

class Player : IPlayer, IDisposable
{
    public void {|TF1023:Play|}(string clip) { }
    public void Stop() { }
    public void Play(int clip) { }
    public void Dispose() { }
}

class Mixer : IDisposable
{
    public void {|TF1023:Fade|}(int a) { }
    public void Dispose() { }
    public void Fade(string a) { }
}");
        }

        [Test]
        public void OverloadsOfOneInterfaceOrInASectionOfOne_AreReported()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
interface IBuffer { void SetInt(string name, int value); void SetInt(int id, int value); void SetFloat(string name, float value); }
interface I1 { void R(int a); void U(int a); }
interface I3 { void R(bool a); }

class Buffer : IBuffer
{
    public void {|TF1023:SetInt|}(string name, int value) { }
    public void SetFloat(string name, float value) { }
    public void SetInt(int id, int value) { }
}

class LoneSection : I1, I3
{
    public void {|TF1023:R|}(int a) { }
    public void U(int a) { }
    public void R(bool a) { }
}");
        }

        [Test]
        public void ExplicitImplementationBetweenOverloads_SplitsThem()
        {
            AnalyzerVerifier.Verify(new SplitOverloadsAnalyzer(), @"
using System.Threading.Tasks;

public interface IActorManager
{
    Task<object> AddActorAsync(string actorId);
}

public interface IActorManager<TActor, TState> : IActorManager
{
    new Task<TActor> AddActorAsync(string actorId);
    Task<TActor> AddActorAsync(string actorId, TState state);
}

public abstract class ActorManager<TActor, TState> : IActorManager<TActor, TState>
{
    public virtual Task<TActor> {|TF1023:AddActorAsync|}(string actorId) => Task.FromResult(default(TActor));

    async Task<object> IActorManager.AddActorAsync(string actorId) => await AddActorAsync(actorId);

    public virtual Task<TActor> AddActorAsync(string actorId, TState state) => AddActorAsync(actorId);
}

public interface IShop
{
    int Buy(string item);
}

public class Shop : IShop
{
    public int {|TF1023:Buy|}(string item, int count) => count;

    int IShop.Buy(string item) => 1;

    public int Buy(int id) => id;
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