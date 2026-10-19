using System.Threading.Tasks;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules
{
    // TF4039 differs from UNT0039 here: a [RequireComponent] on any base class counts, since Unity
    // inherits it. Upstream reported these, for example ProjectStream's SettingsView.
    public class RequireComponentInheritanceTests : BaseDiagnosticVerifierTest<RequireComponentAnalyzer>
    {
        [Test]
        public async Task RequiredByBaseClass_IsNotReported()
        {
            const string test = @"
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Mover : MonoBehaviour
{
}

public class PlayerMover : Mover
{
    void Start()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

            await VerifyCSharpDiagnosticAsync(test);
        }

        [Test]
        public async Task RequiredByGrandparentClass_IsNotReported()
        {
            const string test = @"
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public abstract class Mover : MonoBehaviour
{
}

public abstract class WalkingMover : Mover
{
}

public class PlayerMover : WalkingMover
{
    void Start()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

            await VerifyCSharpDiagnosticAsync(test);
        }

        [Test]
        public async Task BaseClassRequiresSomethingElse_IsStillReported()
        {
            const string test = @"
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Mover : MonoBehaviour
{
}

public class PlayerMover : Mover
{
    void Start()
    {
        var body = GetComponent<Rigidbody>();
    }
}";

            DiagnosticResult diagnostic = ExpectDiagnostic().WithLocation(13, 20);
            await VerifyCSharpDiagnosticAsync(test, diagnostic);
        }
    }
}