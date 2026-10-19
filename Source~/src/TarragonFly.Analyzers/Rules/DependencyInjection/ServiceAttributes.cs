using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // The [Service] attributes on a type and where to report on them, for TF6500 to TF6502.
    internal static class ServiceAttributes
    {
        public static IEnumerable<AttributeData> On(INamedTypeSymbol type)
        {
            return type.GetAttributes().Where(a => a.ApplicationSyntaxReference != null && Frameworks.InitArgs.IsServiceAttribute(a));
        }

        // The named argument, such as "LazyInit = true", or the whole attribute when it isn't written out.
        public static Location ArgumentLocation(AttributeData attribute, string argument, CancellationToken ct)
        {
            AttributeSyntax syntax = (AttributeSyntax)attribute.ApplicationSyntaxReference.GetSyntax(ct);
            AttributeArgumentSyntax named = syntax.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == argument);
            return (named ?? (SyntaxNode)syntax).GetLocation();
        }
    }
}