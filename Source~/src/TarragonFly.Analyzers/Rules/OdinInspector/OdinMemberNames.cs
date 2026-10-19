using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.OdinInspector
{
    // The member names in Odin attributes on one symbol, with the type Odin looks them up on and the
    // argument to report on, for TF6000 and TF6001.
    internal static class OdinMemberNames
    {
        public readonly struct NameInAttribute
        {
            public NameInAttribute(AttributeData attribute, Frameworks.Odin.MemberReference reference, INamedTypeSymbol lookedUpOn, Location location)
            {
                Attribute = attribute;
                Reference = reference;
                LookedUpOn = lookedUpOn;
                Location = location;
            }

            public AttributeData Attribute { get; }
            public Frameworks.Odin.MemberReference Reference { get; }
            public INamedTypeSymbol LookedUpOn { get; }
            public Location Location { get; }

            // "ShowIf" for ShowIfAttribute.
            public string AttributeName
            {
                get
                {
                    string name = Attribute.AttributeClass.Name;
                    return name.EndsWith("Attribute", System.StringComparison.Ordinal) ? name.Substring(0, name.Length - "Attribute".Length) : name;
                }
            }
        }

        // Odin looks names up on the type that declares the attributed member, or on the type itself
        // for an attribute on a class.
        public static IEnumerable<NameInAttribute> On(ISymbol symbol, CancellationToken ct)
        {
            INamedTypeSymbol lookedUpOn = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            if (lookedUpOn == null) yield break;
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.ApplicationSyntaxReference == null) continue;
                foreach (Frameworks.Odin.MemberReference reference in Frameworks.Odin.GetMemberReferences(attribute))
                {
                    yield return new NameInAttribute(attribute, reference, lookedUpOn, ArgumentLocation(attribute, reference, ct));
                }
            }
        }

        // Members with the name on the type or its base types.
        public static List<ISymbol> Find(INamedTypeSymbol type, string name)
        {
            List<ISymbol> found = new List<ISymbol>();
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                found.AddRange(current.GetMembers(name).Where(m => m is IFieldSymbol || m is IPropertySymbol || m is IMethodSymbol));
            }
            return found;
        }

        private static Location ArgumentLocation(AttributeData attribute, Frameworks.Odin.MemberReference reference, CancellationToken ct)
        {
            AttributeSyntax syntax = attribute.ApplicationSyntaxReference.GetSyntax(ct) as AttributeSyntax;
            if (syntax?.ArgumentList == null) return syntax?.GetLocation() ?? Location.None;
            SeparatedSyntaxList<AttributeArgumentSyntax> arguments = syntax.ArgumentList.Arguments;

            if (reference.NamedArgument != null)
            {
                AttributeArgumentSyntax named = arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == reference.NamedArgument);
                return (named?.Expression ?? (SyntaxNode)syntax).GetLocation();
            }

            string parameter = attribute.AttributeConstructor?.Parameters.ElementAtOrDefault(reference.Position)?.Name;
            AttributeArgumentSyntax byName = arguments.FirstOrDefault(a => a.NameColon != null && a.NameColon.Name.Identifier.ValueText == parameter);
            if (byName != null) return byName.Expression.GetLocation();
            List<AttributeArgumentSyntax> positional = arguments.Where(a => a.NameEquals == null && a.NameColon == null).ToList();
            return reference.Position < positional.Count ? positional[reference.Position].Expression.GetLocation() : syntax.GetLocation();
        }
    }
}