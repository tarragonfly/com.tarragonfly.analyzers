using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace TarragonFly.Analyzers.Tests
{
    public class DescriptorsTests
    {
        [Test]
        public void Create_RecordsChecksAndSourceRule()
        {
            DiagnosticDescriptor rule = Descriptors.Create("TF4008", "Title", "Message", "Size", CodeKind.Game, sourceRule: "UNT0008");

            Assert.That(Descriptors.GetChecks(rule), Is.EqualTo(CodeKind.Game));
            Assert.That(Descriptors.GetSourceRule(rule), Is.EqualTo("UNT0008"));
            Assert.That(rule.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [Test]
        public void NewRule_HasNoSourceRule()
        {
            DiagnosticDescriptor rule = Descriptors.Create("TF3100", "Title", "Message", "Logging", CodeKind.All);

            Assert.That(Descriptors.GetSourceRule(rule), Is.Null);
            Assert.That(Descriptors.GetChecks(rule), Is.EqualTo(CodeKind.All));
        }
    }
}