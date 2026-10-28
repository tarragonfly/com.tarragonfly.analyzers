using System;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests
{
    public class RuleRegressionTests
    {
        [TestCase("TF2017", "class C { bool F(int x) => {|TF2017:x % 2 == 1e100|}; }")]
        [TestCase("TF2029", "class C { ulong F(ulong x) => x | 0x8000000000000000UL; }")]
        [TestCase("TF2500", "using System.Runtime.InteropServices; class B { public virtual void F(int x = 1) {} } class C : B { public override void F([Optional, DefaultParameterValue(2)] int {|TF2500:x|}) {} }")]
        [TestCase("TF1016", "class C { int value = 1; public ref int F() => ref value; }")]
        [TestCase("TF1502", "class C { int F() { int value = 2; ref int alias = ref value; value = 3; return alias; } }")]
        [TestCase("TF2012", "class C { int F(ref int x) => x++; int G(ref int x) { return x++; } }")]
        [TestCase("TF2002", "using System; class C { void F() { try { throw new Exception(); } catch (Exception e) when (e.Message != null) { e = new Exception(); Console.Write(e); } } }")]
        [TestCase("TF2009", "using System.Collections.Generic; class C : List<string> { IEnumerable<object> F(C value) => (IEnumerable<object>)value; }")]
        [TestCase("TF2018", "class C { bool F(float x) => x < 1e100 || x > -1e100; }")]
        [TestCase("TF2040", "class C { public bool Enabled; bool F() => this is { Enabled: true }; }")]
        [TestCase("TF2063", "using System; [Flags] enum Bits : int { None = 0, Low = 1, High = int.MinValue, Both = Low | High, All = -1 }")]
        [TestCase("TF2067", "using System.Text.RegularExpressions; class C { Regex F(RegexOptions options) => new Regex(@\"(?n)(a)\\1\", options); }")]
        [TestCase("TF2039", "using System.IO; class C { Stream F() { Stream stream; using (stream = new MemoryStream()) { stream = new MemoryStream(); return stream; } } }")]
        [TestCase("TF8018", "using System; using System.Collections.Generic; using System.Linq; class C { bool F() => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { \"boss\" }.Any(x => x == \"BOSS\"); }")]
        [TestCase("TF8018", "using System.Collections.Generic; using System.Linq; class C { bool F(List<double> values) => values.Any(x => x == double.NaN); }")]
        [TestCase("TF8018", "using System.Collections.Generic; using System.Linq; class C { bool F(List<int> values) => values.Any(x => x == GetValue()); int GetValue() => 2; }")]
        [TestCase("TF8015", "using System.Collections.Generic; using System.Linq; class C { int F() => new SortedSet<int> { 1, 2 }.{|TF8015:Max|}(); }")]
        [TestCase("TF8015", "using System.Collections.Generic; using System.Linq; class C { int F() => new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a))) { 1, 2 }.Max(); }")]
        [TestCase("TF8015", "using System.Collections.Generic; using System.Linq; class C { int F() => new SortedSet<int>().Max(); }")]
        [TestCase("TF1011", "class B { public virtual void F<T>(T value) {} } class C : B { public override void F<T>(T value) => base.F<object>(value); }")]
        [TestCase("TF1024", "class C { int value; int F(int x) { value++; return value; } int G(int value) { value++; return value; } }")]
        [TestCase("TF1024", "class C { int F(int x) { int temp = x + 1; return temp; } int {|TF1024:G|}(int x) { int temp = x + 1; return temp; } }")]
        [TestCase("TF1024", "class Copies { T First<T>(T value) { T copy = value; return copy; } T {|TF1024:Second|}<T>(T value) { T copy = value; return copy; } }")]
        [TestCase("TF1024", "using System.Collections.Generic; class Copies { List<T> First<T>(T value) { List<T> copy = new List<T>(); copy.Add(value); return copy; } List<T> {|TF1024:Second|}<T>(T value) { List<T> copy = new List<T>(); copy.Add(value); return copy; } }")]
        [TestCase("TF1011", "class Receiver { public virtual void Accept<T>(T value) {} } class Forwarder : Receiver { {|TF1011:public override void Accept<T>(T value) => base.Accept<T>(value);|} }")]
        [TestCase("TF2039", "using System.IO; class Loader { MemoryStream Load() { {|TF2039:using|} MemoryStream stream = new MemoryStream(); Inspect(in stream); return stream; } void Inspect(in MemoryStream stream) {} }")]
        [TestCase("TF2052", "using System.CodeDom.Compiler; class Packet { private Packet() {} public int Value; [GeneratedCode(\"FactoryGenerator\", \"1.0\")] public static Packet Create() => new Packet(); }")]
        [TestCase("TF2052", "using System.CodeDom.Compiler; [GeneratedCode(\"FactoryGenerator\", \"1.0\")] class Packet { private Packet() {} public int Value; }")]
        [TestCase("TF2064", "using System.Collections.Generic; class Counts { void Fill(Dictionary<string, int> counts, int[] first, int[] second) { counts.Add(\"item\", first[0]); {|TF2064:counts.Add(\"item\", second[0]);|} } }")]
        [TestCase("TF3051", "using System; class Loader { void Load() { try { Load(); } catch (Exception error) { Action replace = () => error = new Exception(); {|TF3051:throw error;|} } } }")]
        [TestCase("TF3051", "using System; class Loader { void Load() { try { Load(); } catch (Exception error) { void Replace() { error = new Exception(); } {|TF3051:throw error;|} } } }")]
        [TestCase("TF3051", "using System; class Loader { void Load() { try { Load(); } catch (Exception error) { Action replace = () => error = new Exception(); replace(); throw error; } } }")]
        [TestCase("TF3051", "using System; class Loader { void Load() { try { Load(); } catch (Exception error) { Replace(); throw error; void Replace() { error = new Exception(); } } } }")]
        [TestCase("TF7101", "using System; using UnityEngine; class Session { public static Action {|TF7101:Changed|}; static int score; [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (Changed, score) = (Changed, 0); } }")]
        [TestCase("TF7101", "using System; using UnityEngine; class Session { public static Action {|TF7101:Changed|}; public static Action Cleared; [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (Changed, Cleared) = (Changed, null); } }")]
        [TestCase("TF7101", "using System; using UnityEngine; class Session { public static Action Changed; public static Action Cleared; static (Action, Action) Empty() => (null, null); [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (Changed, Cleared) = Empty(); } }")]
        [TestCase("TF7101", "using System; using UnityEngine; class Session { public static Action Changed; [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (Changed, _) = ((Action)(() => Changed = null), 0); } }")]
        [TestCase("TF2024", "class C { string F() => {|TF2024:string.Format|}(\"{0,100000000}\", 1); }")]
        [TestCase("TF2044", "class C { void F(ref int x) {} void F(params int[] xs) {} void G() { F(1); } }")]
        [TestCase("TF2049", "class C { public void F(int name, int extra = 0) {} public void F(int other) {} public void G() { F(name: 1); } }")]
        [TestCase("TF2049", "class C { public void F(int value, int extra = 0) {} private void F(int value) {} }")]
        [TestCase("TF2052", "class {|TF2052:C|} { private C() {} object value = new(); }")]
        [TestCase("TF2052", "class C { private C() {} static C Create() => new(); }")]
        [TestCase("TF2064", "class C { int[] values; void F(C first, C second) { first.values[0] = 1; second.values[0] = 2; } }")]
        [TestCase("TF2064", "class C { void F(int[] values, int index) { values[index] = index++; values[index] = 2; } }")]
        [TestCase("TF2509", "class B { public void F(ref string value) {} } class C : B { public void F(object value) {} }")]
        [TestCase("TF2506", "using System; class C { public event Action {|TF2506:Changed|}; string F() => nameof(Changed); }")]
        [TestCase("TF3051", "using System; class C { void F() { try { F(); } catch (Exception error) { error = new Exception(); throw error; } } }")]
        [TestCase("TF3052", "using System; class C : IDisposable { void IDisposable.Dispose() { {|TF3052:throw new Exception();|} } }")]
        [TestCase("TF3000", "using static System.Console; class C { void F() { {|TF3000:WriteLine|}(\"hello\"); } }")]
        [TestCase("TF3100", "using static UnityEngine.Debug; class C { void F() { Log({|TF3100:\"hello\"|}); } }")]
        [TestCase("TF4004", "using UnityEngine; using Clock = UnityEngine.Time; class C : MonoBehaviour { void Update() { float step = Clock.{|TF4004:fixedDeltaTime|}; } }")]
        [TestCase("TF4004", "using UnityEngine; class C : MonoBehaviour { static class Time { public static float fixedDeltaTime; } void Update() { float step = Time.fixedDeltaTime; } }")]
        [TestCase("TF4010", "using UnityEngine; class C { Component value = {|TF4010:new()|}; }")]
        [TestCase("TF4011", "using UnityEngine; class C { ScriptableObject value = {|TF4011:new()|}; }")]
        [TestCase("TF4018", "using UnityEngine; class C : MonoBehaviour { void Update() => {|TF4018:typeof(C).GetMethod(\"Update\")|}; }")]
        [TestCase("TF4034", "using UnityEngine; class C { Vector2 F(Vector3 value) => new Vector2(y: value.x, x: value.y); }")]
        [TestCase("TF4034", "using UnityEngine; class C { Vector2 F(Vector3 value) => {|TF4034:new Vector2(y: value.y, x: value.x)|}; }")]
        [TestCase("TF4035", "using UnityEngine; class C { Vector3 F(Vector2 value) => new Vector3(y: value.x, z: 0, x: value.y); }")]
        [TestCase("TF4039", "using UnityEngine; class C : MonoBehaviour { void Start() { Component value = GetComponent<Component>(); if (value == null) return; } }")]
        [TestCase("TF4039", "using UnityEngine; interface ITarget {} class C : MonoBehaviour { void Start() { ITarget value = GetComponent<ITarget>(); } }")]
        [TestCase("TF4039", "using UnityEngine; class C : MonoBehaviour { void Start() { Component other = null; Component value = {|TF4039:GetComponent<Component>()|}; if (other == null) return; } }")]
        [TestCase("TF4039", "using UnityEngine; class C : MonoBehaviour { Component first; void Start() { Component second = GetComponent<Component>(); if (first != null) { } else if (second != null) { } } }")]
        [TestCase("TF4039", "using UnityEngine; class C : MonoBehaviour { Component body; void Start() { body = GetComponent<Component>(); Debug.Log(\"ready\"); if (!body) return; } }")]
        [TestCase("TF4039", "using UnityEngine; class C : MonoBehaviour { Component body; void Start() { body = {|TF4039:GetComponent<Component>()|}; } void Update() { if (body != null) { } } }")]
        [TestCase("TF4022", "using UnityEngine; class C { Transform Next() => null; void F(Vector3 position, Quaternion rotation) { Next().position = position; Next().rotation = rotation; } }")]
        [TestCase("TF4036", "using UnityEngine; class C { Transform Next() => null; void F() { Vector3 position = Next().position; Quaternion rotation = Next().rotation; } }")]
        [TestCase("TF4042", "using UnityEngine; class C { void F(Mesh mesh, Vector3[] vertices) { for (int i = 0; i < 3; i++) mesh.vertices = vertices; } }")]
        [TestCase("TF4042", "using UnityEngine; class C { void F(Mesh mesh) { for (int i = 0; i < 3; i++) { int[] triangles = {|TF4042:mesh.triangles|}; } } }")]
        [TestCase("TF4046", "using UnityEngine; class C { static int id; static C() { id = Shader.PropertyToID(\"_Color\"); } }")]
        [TestCase("TF4046", "using System; using UnityEngine; class C { Func<int> lookup = () => {|TF4046:Shader.PropertyToID(\"_Color\")|}; }")]
        [TestCase("TF4103", "using UnityEngine; class C : MonoBehaviour { Component target; void OnValidate() { ({|TF4103:target|}, _) = ((Component)null, 1); } }")]
        [TestCase("TF4104", "using UnityEngine; class Data { public int count; } class Other : MonoBehaviour { public Data data; } class C : MonoBehaviour { Other other; void OnValidate() { {|TF4104:other.data.count++|}; } }")]
        [TestCase("TF5000", "using System.Threading; class C { static CancellationTokenSource {|TF5000:source = new CancellationTokenSource()|}; }")]
        [TestCase("TF5005", "using System; using System.Threading; using Cysharp.Threading.Tasks; class C { void F(CancellationToken token) { Action later = static () => UniTask.Delay(1); } }")]
        [TestCase("TF5100", "using Cysharp.Threading.Tasks; class C { UniTask Fresh() => default; void Use(UniTask a, UniTask b, UniTask c) {} void F(UniTask task) { Use(task, task = Fresh(), task); } }")]
        [TestCase("TF5100", "using Cysharp.Threading.Tasks; class C { async UniTask F(UniTask task) { try { await task; } finally { await {|TF5100:task|}; } } }")]
        [TestCase("TF5100", "using Cysharp.Threading.Tasks; class C { async UniTask F(UniTask task) { try { await task; } finally { task = default; } await task; } }")]
        [TestCase("TF5100", "using Cysharp.Threading.Tasks; class C { async UniTask F(UniTask task) { try { } finally { await task; } await {|TF5100:task|}; } }")]
        [TestCase("TF5102", "using System.Runtime.CompilerServices; using System.Threading.Tasks; struct Wait {} static class Extensions { public static TaskAwaiter GetAwaiter(this Wait value) => Task.CompletedTask.GetAwaiter(); } class C { Wait LoadAsync() => default; }")]
        [TestCase("TF5103", "using UnityEngine; using Cysharp.Threading.Tasks; class C : MonoBehaviour { UniTask<Vector3> LoadAsync() => default; async void {|TF5103:Start|}() { transform.position = await LoadAsync(); } }")]
        [TestCase("TF5105", "using System.Threading; using Cysharp.Threading.Tasks; class C { async UniTask F(CancellationToken token) { await UniTask.WaitUntilCanceled(token); token.ThrowIfCancellationRequested(); } }")]
        [TestCase("TF5105", "using System.Threading; using Cysharp.Threading.Tasks; class C { int Duration() => 1; async UniTask F(CancellationToken token) { token.ThrowIfCancellationRequested(); await UniTask.Delay(Duration(), token); } }")]
        [TestCase("TF6503", "using UnityEngine; using Sisus.Init; class C : MonoBehaviour { string F() => nameof(Service<object>.Instance); }")]
        [TestCase("TF6504", "using UnityEngine; using Sisus.Init; class C : MonoBehaviour { Any<object> value; string F() => nameof(value.Value); }")]
        [TestCase("TF6506", "using static Sisus.Init.Service; class C { void F() { {|TF6506:Set|}<object>(this); } }")]
        [TestCase("TF7005", "class C { static int first = (later = 3); static int later; }")]
        [TestCase("TF7100", "class C { static int {|TF7100:score|}; void F() { (score, _) = (1, 2); } }")]
        [TestCase("TF7100", "using UnityEngine; class C { static int score; void F() { score++; } [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (score, _) = (0, 0); } }")]
        [TestCase("TF7101", "using System; using UnityEngine; class C { public static Action Changed; [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { (Changed, _) = ((Action)null, 0); } }")]
        [TestCase("TF8003", "using System.Linq; class C { int[] cache; int[] values; int[] Copy => cache = {|TF8003:values.ToArray|}(); }")]
        [TestCase("TF8004", "using System.Collections; using System.Collections.Generic; using System.Linq; class C : ICollection<int> { int ICollection<int>.Count => 0; bool ICollection<int>.IsReadOnly => true; public IEnumerator<int> GetEnumerator() => null; IEnumerator IEnumerable.GetEnumerator() => GetEnumerator(); void ICollection<int>.Add(int x) {} void ICollection<int>.Clear() {} bool ICollection<int>.Contains(int x) => false; void ICollection<int>.CopyTo(int[] a, int i) {} bool ICollection<int>.Remove(int x) => false; public int F() => this.Count(); }")]
        [TestCase("TF8005", "class C { void F(string text) { text.ToCharArray()[0] = 'a'; text.ToCharArray()[0]++; } }")]
        [TestCase("TF8006", "class C { void F(int x) {} void F(params int[] xs) {} void G() { F(new[] { 1 }); } }")]
        [TestCase("TF2017", "class C { const char One = '1'; bool F(int x) => {|TF2017:x % 2 == 'a'|}; bool G(int x) => {|TF2017:x % 10 != One|}; }")]
        [TestCase("TF1024", "using System.Collections.Generic; using System.Linq; class C { List<int> items = new List<int>(); int First() { var picked = items.Where(item => item > 1); return picked.Count(); } int {|TF1024:Second|}() { var picked = items.Where(item => item > 1); return picked.Count(); } }")]
        [TestCase("TF1024", "class C { int First() { int Twice(int v) => v * 2; return Twice(3); } int {|TF1024:Second|}() { int Twice(int v) => v * 2; return Twice(3); } }")]
        [TestCase("TF3051", "using System; class C { void F() { try { F(); } catch (Exception e) { if (e is AggregateException a) { e = a.InnerException; Console.WriteLine(e); return; } {|TF3051:throw e;|} } } }")]
        [TestCase("TF3051", "using System; class C { void F() { try { F(); } catch (Exception e) { int code; (e, code) = (new InvalidOperationException(\"wrapped\", e), 1); throw e; } } }")]
        [TestCase("TF3051", "using System; class C { void F() { try { F(); } catch (Exception e) { for (int i = 0; i < 2; i++) { if (i == 1) throw e; e = new InvalidOperationException(\"retry\", e); } } } }")]
        [TestCase("TF3051", "using System; class C { void F() { try { F(); } catch (Exception e) { Action replace = () => e = new InvalidOperationException(\"wrapped\", e); replace?.Invoke(); throw e; } } }")]
        [TestCase("TF3051", "using System; class C { bool retry; void F() { try { F(); } catch (Exception e) { Action fix = () => { }; if (retry) fix = () => e = new InvalidOperationException(\"wrapped\", e); fix(); throw e; } } }")]
        [TestCase("TF8003", "using System.Linq; class C { int[] cache; int[] values; public int[] Copy => cache ?? (cache = values.ToArray()); }")]
        [TestCase("TF2064", "class C { int Load() => 1; void F(int[] values) { values[0] = Load(); {|TF2064:values[0] = 0;|} } }")]
        [TestCase("TF2040", "class Unit { bool F() => {|TF2040:this is not Boss|}; bool G() => {|TF2040:this is Boss or Unit|}; } class Boss : Unit {}")]
        [TestCase("TF5105", "using System; using System.Threading; using Cysharp.Threading.Tasks; class C { async UniTask F(CancellationToken ct) { Func<int, UniTask> load = id => UniTask.Delay(id, ct); await UniTask.Delay(500, ct); {|TF5105:ct.ThrowIfCancellationRequested()|}; } }")]
        [TestCase("TF5105", "using System; using System.Threading; using Cysharp.Threading.Tasks; class C { event Action<CancellationToken> Restarted; async UniTask F(CancellationToken token) { void Replace(CancellationToken next) { token = next; } Restarted += Replace; await UniTask.Delay(1, token); token.ThrowIfCancellationRequested(); } }")]
        [TestCase("TF4104", "using UnityEngine; class C : MonoBehaviour { static int count; void OnValidate() { C.count++; } }")]
        [TestCase("TF5000", "using System; class Pool : IDisposable { public void Dispose() { Console.WriteLine(); } } class C { private static readonly Pool Shared = new Pool(); static string Use() => Shared.ToString(); }")]
        [TestCase("TF2049", "class C { private void F(int value, {|TF2049:int extra = 0|}) {} public void F(int value) {} }")]
        [TestCase("TF2052", "abstract class Command { private Command() {} public int Cost; public sealed class Jump : Command {} public static Command Parse() => new Jump(); }")]
        [TestCase("TF2052", "abstract class Command { private Command() {} public int Cost; public abstract class Move : Command { protected Move() : base() {} } } class Walk : Command.Move {}")]
        [TestCase("TF2012", "class C { int Build() { int nextId = 0; int NewId() { return nextId++; } return NewId() + NewId(); } }")]
        [TestCase("TF2012", "using System; class C { Func<int> F() => () => { int count = 0; return {|TF2012:count++|}; }; }")]
        [TestCase("TF8015", "using System.Collections.Generic; using System.Linq; class Reversed<T> : SortedSet<T> { public Reversed() : base(Comparer<T>.Create((a, b) => Comparer<T>.Default.Compare(b, a))) {} } class C { int F() { SortedSet<int> s = new Reversed<int> { 3, 1, 2 }; return s.Max(); } }")]
        [TestCase("TF5103", "using UnityEngine; using Cysharp.Threading.Tasks; class C : MonoBehaviour { UniTask<bool> LoadAsync() => default; void Show() {} async void {|TF5103:Start|}() { if (await LoadAsync()) Show(); } }")]
        [TestCase("TF5103", "using UnityEngine; using Cysharp.Threading.Tasks; class C : MonoBehaviour { UniTask<bool> LoadAsync() => default; void Show() {} async void {|TF5103:Start|}() { switch (await LoadAsync()) { case true: Show(); break; } } }")]
        [TestCase("TF5005", "using System; using System.Threading; using Cysharp.Threading.Tasks; using UnityEngine; class Fader : MonoBehaviour { CancellationTokenSource cts; Func<UniTask> fade = () => UniTask.Delay(500); }")]
        [TestCase("TF2039", "using System.IO; class C { Stream Open() => new MemoryStream(); Stream F() { Stream stream; using (stream = Open()) { (stream, _) = (Open(), 0); return stream; } } }")]
        [TestCase("TF4004", "using UnityEngine; class C : MonoBehaviour { void Update() { string name = nameof(Time.fixedDeltaTime); } }")]
        [TestCase("TF4040", "using UnityEngine; class C { string F(GameObject go) => nameof(go.isStatic); }")]
        [TestCase("TF4042", "using UnityEngine; class C { void F(Mesh mesh) { for (int i = 0; i < 3; i++) { string name = nameof(mesh.vertices); } } }")]
        [TestCase("TF4022", "using UnityEngine; class C { void F(GameObject bullet, Transform tip) { {|TF4022:bullet.transform.position = tip.position;|} bullet.transform.rotation = Quaternion.Euler(0, Random.Range(-5f, 5f), 0); } }")]
        public void Diagnostics_MatchExpectedSourceSpans(string ruleId, string source)
        {
            Check(ruleId, source);
        }

        [TestCase("TF9000", "using NUnit.Framework; class C { [Test, {|TF9000:Ignore(\"later\", Until = \"\")|}] public void F() {} }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { [Test] public void {|TF9002:F|}() { Action later = () => Assert.IsTrue(true); } }")]
        [TestCase("TF9002", "using NUnit.Framework; class C { [Test] public void {|TF9002:F|}() { void Later() { Assert.IsTrue(true); } } }")]
        [TestCase("TF9002", "using NUnit.Framework; class C { [Test] public void F() { void Later() { Assert.IsTrue(true); } Later(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { static void Run(Action callback) => callback(); [Test] public void F() => Run(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { static T Run<T>(Func<T> callback) => callback(); [Test] public void F() => Run(() => { Assert.IsTrue(true); return 1; }); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { Action later; void Store(Action callback) { later = callback; } [Test] public void F() => Store(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { void Ignore(Action callback) { } [Test] public void {|TF9002:F|}() => Ignore(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { static void Run(Action callback) => callback?.Invoke(); [Test] public void F() => Run(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { static void Run(Action callback) => Inner(callback); static void Inner(Action a) => a(); [Test] public void F() => Run(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { static void RunAll(params Action[] steps) { foreach (Action step in steps) step(); } [Test] public void F() => RunAll(() => Assert.IsTrue(true)); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class Player { public event Action Died; public void Kill() => Died?.Invoke(); } class C { [Test] public void F() { Player player = new Player(); player.Died += () => Assert.Pass(); player.Kill(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class Runner { public Runner(Action a) { a(); } } class C { [Test] public void F() { new Runner(() => Assert.IsTrue(true)); } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class C { [Test] public void F() { Task.Run(() => Assert.IsTrue(true)).Wait(); } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class C { [Test] public async Task F() { await Task.WhenAll(Task.Run(() => Assert.IsTrue(true)), Task.Run(() => Assert.IsFalse(false))); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { [Test] public void F() { Action step = null; step += () => Assert.IsTrue(true); step(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { bool fast; [Test] public void F() { Action run; if (fast) run = () => Assert.IsTrue(true); else run = () => Assert.IsFalse(false); run(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { [Test] public void F() { Action check = () => Assert.IsTrue(true); check?.Invoke(); } }")]
        [TestCase("TF9002", "using System.Collections; using Cysharp.Threading.Tasks; using NUnit.Framework; using UnityEngine.TestTools; class C { [UnityTest] public IEnumerator F() => UniTask.ToCoroutine(async () => { await UniTask.Yield(); Assert.IsTrue(true); }); }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class C { [Test] public void F() { ((Action)(() => Assert.IsTrue(true)))(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class CallbackTests { [Test] public void RunsCallback() { Action callback = () => Assert.IsTrue(true); callback(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class CallbackTests { [Test] public void RunsCallback() { Action callback; callback = () => Assert.IsTrue(true); callback.Invoke(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class CallbackTests { [Test] public void {|TF9002:ReplacesCallback|}() { Action callback = () => Assert.IsTrue(true); callback = () => {}; callback(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class CallbackTests { [Test] public void RunsAlias() { Action callback = () => Assert.IsTrue(true); Action alias = callback; callback = () => {}; alias(); } }")]
        [TestCase("TF9002", "using System; using NUnit.Framework; class CallbackTests { [Test] public void {|TF9002:ReplacesCallback|}() { Action callback = () => Assert.IsTrue(true); (callback, _) = ((Action)(() => {}), 0); callback(); } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class BackgroundTests { [Test] public async Task WaitsForCallback() { Task task = Task.Run(() => Assert.IsTrue(true)); await task; } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class BackgroundTests { [Test] public async Task {|TF9002:ReplacesTask|}() { Task task = Task.Run(() => Assert.IsTrue(true)); task = Task.CompletedTask; await task; } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class BackgroundTests { [Test] public async Task WaitsForAlias() { Task task = Task.Run(() => Assert.IsTrue(true)); Task alias = task; task = Task.CompletedTask; await alias.ConfigureAwait(false); } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class BackgroundTests { [Test] public Task ReturnsTask() { Task task = Task.Run(() => Assert.IsTrue(true)); return task; } }")]
        [TestCase("TF9002", "using System.Linq; using NUnit.Framework; class C { [Test] public void {|TF9002:F|}() { var later = new[] { 1 }.Select(value => { Assert.IsTrue(true); return value; }); } }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class C { [Test] public void {|TF9002:F|}() { Task.Run(() => Assert.IsTrue(true)); } }")]
        [TestCase("TF9005", "using static NUnit.Framework.Assert; class C { void F(int value) { AreEqual({|TF9005:value, 3|}); } }")]
        [TestCase("TF9006", "using NUnit.Framework; using System.Threading.Tasks; class C { [Test] public Task<int> {|TF9006:F|}() => Task.FromResult(1); }")]
        [TestCase("TF9006", "using NUnit.Framework; using System.Threading.Tasks; class C { [Test(ExpectedResult = 1)] public Task<int> F() => Task.FromResult(1); }")]
        [TestCase("TF9002", "using System.Collections; using Cysharp.Threading.Tasks; using NUnit.Framework; using UnityEngine.TestTools; class C { [UnityTest] public IEnumerator F() => Play(); static IEnumerator Play() => UniTask.ToCoroutine(async () => { await UniTask.Yield(); Assert.IsTrue(true); }); }")]
        [TestCase("TF9002", "using System.Collections; using Cysharp.Threading.Tasks; using NUnit.Framework; using UnityEngine.TestTools; class C { [UnityTest] public IEnumerator {|TF9002:F|}() { Play(); yield return null; } static IEnumerator Play() => UniTask.ToCoroutine(async () => { await UniTask.Yield(); Assert.IsTrue(true); }); }")]
        [TestCase("TF9002", "using System.Threading.Tasks; using NUnit.Framework; class C { [Test] public void {|TF9002:F|}() => Task.Run(() => Assert.IsTrue(false)); }")]
        public void TestDiagnostics_MatchExpectedSourceSpans(string ruleId, string source)
        {
            Check(ruleId, source, SnippetAssembly.Tests());
        }

        [Test]
        public void CallbackPassedToAnotherAssembly_MayAssertThere()
        {
            SnippetAssembly assembly = SnippetAssembly.Tests();
            assembly.ReferencedAssemblies.Add("Library/Bee/artifacts/TestHelpers.ref.dll", "using System; public static class Helpers { public static T Run<T>(Func<T> callback) => callback(); }");
            Check("TF9002", "using NUnit.Framework; class C { [Test] public void F() => Helpers.Run(() => { Assert.IsTrue(true); return 1; }); }", assembly);
        }

        [Test]
        public void MovementUsingDeltaTime_StillMergesPositionAndRotation()
        {
            string source = "using UnityEngine; class C : MonoBehaviour { void Move(float speed) { transform.position = new Vector3(speed * Time.deltaTime, 0, 0); transform.rotation = Quaternion.identity; } }";
            Assert.That(AnalyzerVerifier.Run(AnalyzerFor("TF4022"), source).Count(diagnostic => diagnostic.Id == "TF4022"), Is.EqualTo(1));
        }

        [TestCase("UNITY_EDITOR", true)]
        [TestCase("UNITY_EDITOR && DEBUG", true)]
        [TestCase("UNITY_EDITOR || DEBUG", false)]
        [TestCase("!DEBUG", false)]
        [TestCase("UNITY_EDITOR_WIN", true)]
        [TestCase("UNITY_EDITOR_WIN || UNITY_EDITOR_OSX", true)]
        [TestCase("UNITY_EDITOR_64 && !UNITY_EDITOR_LINUX", true)]
        [TestCase("UNITY_EDITOR == true", true)]
        [TestCase("UNITY_EDITOR != false", true)]
        public void EditorGuards_OnlySkipEditorOnlyBranches(string condition, bool editorOnly)
        {
            string member = editorOnly ? "isStatic" : "{|TF4040:isStatic|}";
            string defines = "#define UNITY_EDITOR\n#define UNITY_EDITOR_WIN\n#define UNITY_EDITOR_64\n" + (condition == "!DEBUG" ? "" : "#define DEBUG\n");
            string source = defines + "using UnityEngine; class C { bool F(GameObject go) {\n#if " + condition
                + "\nreturn go." + member + ";\n#else\nreturn false;\n#endif\n} }";
            Check("TF4040", source);
        }

        private static void Check(string ruleId, string source, SnippetAssembly assembly = null)
        {
            AnalyzerVerifier.Verify(AnalyzerFor(ruleId), source, assembly);
        }

        private static DiagnosticAnalyzer AnalyzerFor(string ruleId)
        {
            return typeof(ScopedAnalyzer).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type))
                .Single(candidate => candidate.SupportedDiagnostics.Any(rule => rule.Id == ruleId));
        }
    }
}
