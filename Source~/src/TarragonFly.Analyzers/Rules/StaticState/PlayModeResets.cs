using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // What resets static state on Play Mode entry when domain reload is off, for TF7100 and TF7101.
    internal static class PlayModeResets
    {
        // RuntimeInitializeLoadType values that run before the first scene's Awake: BeforeSceneLoad,
        // AfterAssembliesLoaded, BeforeSplashScreen and SubsystemRegistration. AfterSceneLoad, the
        // default, runs after Awake, which is too late.
        private static readonly int[] EarlyLoadTypes = { 1, 2, 3, 4 };

        public static bool IsResetMethod(ISymbol symbol)
        {
            if (!(symbol is IMethodSymbol method) || !method.IsStatic) return false;
            foreach (AttributeData attribute in method.GetAttributes())
            {
                if (SymbolNames.IsNamed(attribute.AttributeClass, "UnityEditor.InitializeOnEnterPlayModeAttribute")) return true;
                if (!SymbolNames.IsNamed(attribute.AttributeClass, "UnityEngine.RuntimeInitializeOnLoadMethodAttribute")) continue;
                if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int loadType && System.Array.IndexOf(EarlyLoadTypes, loadType) >= 0) return true;
            }
            return false;
        }

        // Unity 6000.6's [AutoStaticsCleanup] resets the member itself, and [NoAutoStaticsCleanup] says
        // keeping the value is intended; either counts on the member or on a type around it.
        // [ThreadStatic] fields are scratch state for the call that sets them: each thread has its own
        // copy, so a reset on the main thread wouldn't reach the others anyway.
        public static bool NeedsNoReset(ISymbol member)
        {
            if (member is IFieldSymbol field && SymbolNames.HasAttribute(field, "System.ThreadStaticAttribute")) return true;
            for (ISymbol current = member; current != null; current = current.ContainingType)
            {
                if (SymbolNames.HasAttribute(current, "Unity.Scripting.LifecycleManagement.AutoStaticsCleanupAttribute")) return true;
                if (SymbolNames.HasAttribute(current, "Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanupAttribute")) return true;
            }
            return false;
        }

        // The reference is the target of an assignment, ++ or --, or a ref or out argument.
        public static bool IsWrite(IOperation reference)
        {
            switch (reference.Parent)
            {
                case IAssignmentOperation assignment: return assignment.Target == reference;
                case IIncrementOrDecrementOperation _: return true;
                case IArgumentOperation argument: return argument.Parameter != null && (argument.Parameter.RefKind == RefKind.Ref || argument.Parameter.RefKind == RefKind.Out);
                default: return false;
            }
        }

        // Initializers belong to the field, and a static constructor runs once per domain: values set
        // there never change, so they can't leak from one Play Mode session into the next.
        public static bool IsStaticInitialization(ISymbol containing, ISymbol member)
        {
            if (containing is IFieldSymbol || containing is IPropertySymbol) return true;
            return containing is IMethodSymbol method && method.MethodKind == MethodKind.StaticConstructor
                && SymbolEqualityComparer.Default.Equals(method.ContainingType, member.ContainingType);
        }
    }
}