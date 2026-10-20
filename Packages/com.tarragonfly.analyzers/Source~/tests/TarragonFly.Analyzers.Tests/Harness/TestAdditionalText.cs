using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Tests.Harness
{
    public sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText text;

        public TestAdditionalText(string path, string text)
        {
            Path = path;
            this.text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return text;
        }
    }
}