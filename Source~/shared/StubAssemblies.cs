using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TarragonFly.Analyzers.Testing
{
    // Compiles Source~/stubs/*.cs into in-memory assemblies named like the real ones, so test code
    // compiles against UnityEngine, UniTask, Odin and Init(args) without a Unity install. Linked into
    // the test project, which copies the stubs next to its output.
    public static class StubAssemblies
    {
        // Only the .NET runtime's own assemblies: the test host also lists its dependencies, such as
        // NUnit itself, which would clash with the NUnit stub.
        public static readonly ImmutableArray<MetadataReference> Platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0 && string.Equals(Path.GetDirectoryName(path), RuntimeFolder, StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        public static readonly MetadataReference UnityEngine = Compile("UnityEngine.CoreModule", "UnityEngine.cs");
        public static readonly MetadataReference UnityEditor = Compile("UnityEditor.CoreModule", "UnityEditor.cs", UnityEngine);
        public static readonly MetadataReference TestRunner = Compile("UnityEngine.TestRunner", "UnityEngine.TestRunner.cs", UnityEngine);
        public static readonly MetadataReference NUnit = Compile("nunit.framework", "nunit.framework.cs");
        public static readonly MetadataReference UniTask = Compile("UniTask", "UniTask.cs");
        public static readonly MetadataReference Odin = Compile("Sirenix.OdinInspector", "Odin.cs", UnityEngine);
        public static readonly MetadataReference InitArgs = Compile("InitArgs", "InitArgs.cs", UnityEngine);

        // What a runtime game assembly in a Unity project can see.
        public static ImmutableArray<MetadataReference> GameReferences => Platform.AddRange(new[] { UnityEngine, UnityEditor, UniTask, Odin, InitArgs });

        // Unity also gives every editor-only assembly the test runner and NUnit.
        public static ImmutableArray<MetadataReference> EditorReferences => GameReferences.AddRange(new[] { TestRunner, NUnit });

        public static string StubsFolder => Path.Combine(AppContext.BaseDirectory, "stubs");

        private static string RuntimeFolder => Path.GetDirectoryName(typeof(object).Assembly.Location);

        private static MetadataReference Compile(string assemblyName, string fileName, params MetadataReference[] references)
        {
            string path = Path.Combine(StubsFolder, fileName);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9), path);
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName, new[] { tree }, Platform.AddRange(references),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using MemoryStream image = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(image);
            if (!result.Success)
            {
                IEnumerable<string> errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());
                throw new InvalidOperationException($"Stub {fileName} does not compile:\n{string.Join("\n", errors)}");
            }
            return MetadataReference.CreateFromImage(image.ToArray());
        }
    }
}