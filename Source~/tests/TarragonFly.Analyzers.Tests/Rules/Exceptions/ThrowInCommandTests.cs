using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Exceptions;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Exceptions
{
    // TF3101 throw in a class that should log and carry on.
    public class ThrowInCommandTests
    {
        private const string Code = @"
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Story
{
    public abstract class CommandBase
    {
        public abstract UniTask ExecuteAsync(CancellationToken ct);
    }

    public sealed class PlaySfx : CommandBase
    {
        object audio;

        public override async UniTask ExecuteAsync(CancellationToken ct)
        {
            if (audio == null) {|TF3101:throw|} new InvalidOperationException(""no audio"");
            object target = audio ?? {|TF3101:throw|} new ArgumentNullException(nameof(audio));
            Action later = () => {|TF3101:throw|} new NotSupportedException();
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            try
            {
                await UniTask.Yield(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        sealed class Step
        {
            void Run() => {|TF3101:throw|} new InvalidOperationException();
        }
    }

    public sealed class Loader
    {
        void Load() => throw new InvalidOperationException(""not a command"");
    }
}";

        [Test]
        public void ThrowInACommand_IsReported()
        {
            AnalyzerVerifier.Verify(new ThrowInCommandAnalyzer(), Code, SnippetAssembly.WithSettings("command_base_types = Story.CommandBase"));
        }

        [Test]
        public void WithoutTheSetting_NothingIsChecked()
        {
            AnalyzerVerifier.Verify(new ThrowInCommandAnalyzer(), Code.Replace("{|TF3101:", string.Empty).Replace("|}", string.Empty));
        }
    }
}