using NUnit.Framework;
using TarragonFly.Analyzers.Rules.SilentFailures;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.SilentFailures
{
    public class EmptyBlockTests
    {
        [Test]
        public void EmptyNestedBlocks_AreReported()
        {
            AnalyzerVerifier.Verify(new EmptyBlockAnalyzer(), @"
using System;

class SaveSystem
{
    int Slot { get { return 0; } set {|TF3001:{ }|} }

    void Load(bool exists, int[] slots)
    {
        if (exists) {|TF3001:{ }|} // a comment after the block doesn't count
        else {|TF3001:{
        }|}
        foreach (int slot in slots) {|TF3001:{ }|}
        try {|TF3001:{ }|}
        catch (Exception) {|TF3001:{ }|}
        finally {|TF3001:{ }|}
        {|TF3001:switch (slots.Length)
        {
        }|}
        void Local() {|TF3001:{ }|}
        Local();
    }
}");
        }

        [Test]
        public void MethodBodiesLambdasAndCommentedBlocks_AreFine()
        {
            AnalyzerVerifier.Verify(new EmptyBlockAnalyzer(), @"
using System;

class SaveSystem
{
    SaveSystem() { }

    void Awake() { }

    void Load(bool exists)
    {
        Action a = () => { };
        Action b = delegate { };
        if (exists)
        {
            // nothing to load yet
        }
        if (exists) { /* waiting on the cloud save API */ }
        if (exists)
        {
#if UNITY_EDITOR_NEVER
            Console.WriteLine();
#endif
        }
        a();
        b();
    }
}");
        }
    }
}