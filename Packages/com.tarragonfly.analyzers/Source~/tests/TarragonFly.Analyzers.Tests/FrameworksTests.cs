using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using TarragonFly.Analyzers.Frameworks;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests
{
    public class FrameworksTests
    {
        private const string Usings =
            "using System; using System.Threading; using System.Threading.Tasks; using UnityEngine; using UnityEngine.Scripting; " +
            "using JetBrains.Annotations; using Cysharp.Threading.Tasks; using Sirenix.OdinInspector; using Sisus.Init;\n";

        [Test]
        public void Unity_RecognisesUnityObjects()
        {
            Compilation compilation = Compile("class Player : MonoBehaviour { } class Settings : ScriptableObject { } class Plain { }");

            Assert.That(Frameworks.Unity.IsUnityObject(Type(compilation, "Player")), Is.True);
            Assert.That(Frameworks.Unity.IsMonoBehaviour(Type(compilation, "Player")), Is.True);
            Assert.That(Frameworks.Unity.IsScriptableObject(Type(compilation, "Settings")), Is.True);
            Assert.That(Frameworks.Unity.IsUnityObject(Type(compilation, "Plain")), Is.False);
        }

        [TestCase("public int speed;", true)]
        [TestCase("[SerializeField] private int speed;", true)]
        [TestCase("[SerializeReference] private object speed;", true)]
        [TestCase("private int speed;", false)]
        [TestCase("[NonSerialized] public int speed;", false)]
        [TestCase("public static int speed;", false)]
        [TestCase("public readonly int speed;", false)]
        [TestCase("public const int speed = 1;", false)]
        public void Unity_IsSavedByUnity_FollowsFieldDeclaration(string field, bool expected)
        {
            Compilation compilation = Compile("class Player : MonoBehaviour { " + field + " }");

            Assert.That(Frameworks.Unity.IsSavedByUnity(Field(compilation, "Player", "speed")), Is.EqualTo(expected));
        }

        [Test]
        public void Unity_IsSavedByUnity_CountsSerializedAutoPropertyBackingField()
        {
            Compilation compilation = Compile("class Player : MonoBehaviour { [field: SerializeField] public int Speed { get; private set; } }");
            IFieldSymbol backingField = Type(compilation, "Player").GetMembers().OfType<IFieldSymbol>().Single();

            Assert.That(Frameworks.Unity.IsSavedByUnity(backingField), Is.True);
        }

        [TestCase("[Preserve]", true)]
        [TestCase("[UsedImplicitly]", true)]
        [TestCase("[ContextMenu(\"Reset\")]", true)]
        [TestCase("[RuntimeInitializeOnLoadMethod]", true)]
        [TestCase("[UnityEditor.MenuItem(\"Tools/Run\")]", true)]
        [TestCase("[Implied]", true)]
        [TestCase("[Obsolete]", false)]
        [TestCase("", false)]
        public void Unity_HasImplicitUseAttribute(string attribute, bool expected)
        {
            Compilation compilation = Compile(
                "[MeansImplicitUse] class ImpliedAttribute : Attribute { } " +
                "class Tools { " + attribute + " static void Run() { } }");

            Assert.That(Frameworks.Unity.HasImplicitUseAttribute(Method(compilation, "Tools", "Run")), Is.EqualTo(expected));
        }

        [Test]
        public void Unity_ContextMenuItem_NamesItsMethod()
        {
            Compilation compilation = Compile("class Player : MonoBehaviour { [ContextMenuItem(\"Reset\", \"ResetSpeed\")] public int speed; void ResetSpeed() { } }");
            AttributeData attribute = Field(compilation, "Player", "speed").GetAttributes().Single();

            Assert.That(Frameworks.Unity.GetMethodNamesReferencedBy(attribute), Is.EqualTo(new[] { "ResetSpeed" }));
        }

        [TestCase("UniTask", true)]
        [TestCase("UniTask<int>", true)]
        [TestCase("UniTaskVoid", false)]
        [TestCase("Task", true)]
        [TestCase("Task<int>", true)]
        [TestCase("ValueTask<int>", true)]
        [TestCase("int", false)]
        [TestCase("System.Collections.IEnumerator", false)]
        public void UniTask_IsTask(string returnType, bool expected)
        {
            Compilation compilation = Compile("abstract class Loader { public abstract " + returnType + " Load(); }");

            Assert.That(UniTask.IsTask(Method(compilation, "Loader", "Load").ReturnType), Is.EqualTo(expected));
        }

        [Test]
        public void UniTask_RecognisesForgetPreserveAndToCoroutine()
        {
            Compilation compilation = Compile(
                "class Loader { void Run(UniTask task, UniTaskVoid fireAndForget, CancellationToken ct) { " +
                "task.Forget(); fireAndForget.Forget(); task.Preserve(); task.ToCoroutine(); Forget(); } void Forget() { } }");
            IMethodSymbol[] calls = Calls(compilation);

            Assert.That(calls.Select(UniTask.IsForget), Is.EqualTo(new[] { true, true, false, false, false }));
            Assert.That(calls.Select(UniTask.IsPreserve), Is.EqualTo(new[] { false, false, true, false, false }));
            Assert.That(calls.Select(UniTask.IsToCoroutine), Is.EqualTo(new[] { false, false, false, true, false }));
            Assert.That(UniTask.IsCancellationToken(Method(compilation, "Loader", "Run").Parameters[2].Type), Is.True);
        }

        [TestCase("[ShowIf(nameof(IsVisible))]", new[] { "IsVisible" })]
        [TestCase("[ShowIf(\"@IsVisible && enabled\")]", new string[0])]
        [TestCase("[ValueDropdown(\"GetIds\")]", new[] { "GetIds" })]
        [TestCase("[ValidateInput(\"Check\", \"Must not be empty.\")]", new[] { "Check" })]
        [TestCase("[InfoBox(\"$message\", InfoMessageType.Warning)]", new[] { "message" })]
        [TestCase("[InfoBox(\"Plain text\", \"IsVisible\")]", new[] { "IsVisible" })]
        [TestCase("[InfoBox(\"Plain text\", VisibleIf = \"IsVisible\")]", new[] { "IsVisible" })]
        [TestCase("[LabelText(\"$label\")]", new[] { "label" })]
        [TestCase("[LabelText(\"Speed\")]", new string[0])]
        [TestCase("[Obsolete(\"$notOdin\")]", new string[0])]
        public void Odin_FindsMembersNamedInAttributeStrings(string attribute, string[] expected)
        {
            Compilation compilation = Compile("class Player : MonoBehaviour { bool IsVisible => true; " + attribute + " public int speed; }");
            AttributeData data = Field(compilation, "Player", "speed").GetAttributes().Single();

            Assert.That(Odin.GetReferencedMemberNames(data), Is.EqualTo(expected));
        }

        [Test]
        public void Odin_RecognisesSerializedBaseTypes()
        {
            Compilation compilation = Compile("class Inventory : SerializedMonoBehaviour { } class Player : MonoBehaviour { }");

            Assert.That(Odin.IsOdinSerializedType(Type(compilation, "Inventory")), Is.True);
            Assert.That(Odin.IsOdinSerializedType(Type(compilation, "Player")), Is.False);
        }

        [Test]
        public void InitArgs_RecognisesClientsAndTheirInitMethod()
        {
            Compilation compilation = Compile(
                "class Mover : MonoBehaviour<Rigid, int> { protected override void Init(Rigid body, int speed) { } void Init(int speed) { } } " +
                "class Rigid { } class Plain { void Init(int speed) { } }");
            IMethodSymbol[] moverInits = Type(compilation, "Mover").GetMembers("Init").OfType<IMethodSymbol>().ToArray();

            Assert.That(InitArgs.IsClient(Type(compilation, "Mover")), Is.True);
            Assert.That(InitArgs.IsClient(Type(compilation, "Plain")), Is.False);
            Assert.That(moverInits.Select(InitArgs.IsInitMethod), Is.EqualTo(new[] { true, false }));
            Assert.That(InitArgs.IsInitMethod(Method(compilation, "Plain", "Init")), Is.False);
        }

        [Test]
        public void InitArgs_RecognisesAnyServiceAndServiceAccess()
        {
            Compilation compilation = Compile(
                "[Service(typeof(Audio))] class Audio { } " +
                "class Player : MonoBehaviour { public Any<Audio> audio; void Run() { Service.Get<Audio>(); Audio a = Service<Audio>.Instance; } }");
            INamedTypeSymbol audio = Type(compilation, "Audio");
            IMethodSymbol[] calls = Calls(compilation);
            IPropertySymbol instance = Properties(compilation).Single();

            Assert.That(InitArgs.IsAny(Field(compilation, "Player", "audio").Type), Is.True);
            Assert.That(InitArgs.IsServiceAttribute(audio.GetAttributes().Single()), Is.True);
            Assert.That(Frameworks.Unity.HasImplicitUseAttribute(audio), Is.True, "[Service] derives from [Preserve].");
            Assert.That(InitArgs.IsServiceAccess(calls.Single().ContainingType), Is.True);
            Assert.That(InitArgs.IsServiceAccess(instance.ContainingType), Is.True);
            Assert.That(InitArgs.IsServiceAccess(audio), Is.False);
        }

        private static Compilation Compile(string code)
        {
            return AnalyzerVerifier.Compile(Usings + code);
        }

        private static INamedTypeSymbol Type(Compilation compilation, string name)
        {
            return compilation.GetTypeByMetadataName(name);
        }

        private static IFieldSymbol Field(Compilation compilation, string type, string name)
        {
            return Type(compilation, type).GetMembers(name).OfType<IFieldSymbol>().Single();
        }

        private static IMethodSymbol Method(Compilation compilation, string type, string name)
        {
            return Type(compilation, type).GetMembers(name).OfType<IMethodSymbol>().Single();
        }

        private static IMethodSymbol[] Calls(Compilation compilation)
        {
            SyntaxTree tree = compilation.SyntaxTrees.Single();
            SemanticModel model = compilation.GetSemanticModel(tree);
            return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(call => (IMethodSymbol)model.GetSymbolInfo(call).Symbol)
                .ToArray();
        }

        private static IPropertySymbol[] Properties(Compilation compilation)
        {
            SyntaxTree tree = compilation.SyntaxTrees.Single();
            SemanticModel model = compilation.GetSemanticModel(tree);
            return tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Select(access => model.GetSymbolInfo(access).Symbol)
                .OfType<IPropertySymbol>()
                .ToArray();
        }
    }
}