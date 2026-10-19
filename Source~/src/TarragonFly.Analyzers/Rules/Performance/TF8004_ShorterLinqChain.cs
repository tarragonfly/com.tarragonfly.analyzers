using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8004: a LINQ chain with a step that does nothing useful:
    //   - Where(condition) followed by First(), Any(), Count() or another call that takes the
    //     condition itself, as in "enemies.Where(e => e.IsAlive).First()". Reported on Where.
    //   - Where(x => x is T).Select(x => (T)x), which is OfType<T>(). Reported on Where.
    //   - Count() on a collection that has a Count property. Reported on Count.
    //   - ToList() or ToArray() right before another LINQ call or another ToList() or ToArray(), as
    //     in "items.ToList().First()", which copies the items for nothing. Reported on the first
    //     ToList or ToArray.
    //
    // Each extra step allocates an enumerator or a whole copy; in per-frame code that is garbage for
    // the collector.
    //
    // The calls that take the condition: First, FirstOrDefault, Last, LastOrDefault, Single,
    // SingleOrDefault, Any, Count and LongCount, called without arguments. ToList before a List
    // method such as ForEach is fine. A Count property inherited from an interface, as on IList<T>,
    // counts too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ShorterLinqChainAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8004",
            "LINQ chain with an unneeded step",
            "{0}",
            "Performance",
            CodeKind.All);

        private static readonly HashSet<string> TakesCondition = new HashSet<string>
        {
            "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "Any", "Count", "LongCount"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                IMethodSymbol method = LinqCalls.Method(context.SemanticModel, invocation, context.CancellationToken);
                if (method == null)
                {
                    // List.ToArray after ToList, which isn't a LINQ call but copies all the same.
                    if (IsCopy(invocation) && IsCopy(LinqCalls.Receiver(invocation) as InvocationExpressionSyntax)
                        && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol copy)
                    {
                        ReportCopy(context, (InvocationExpressionSyntax)LinqCalls.Receiver(invocation), copy.Name);
                    }
                    return;
                }
                int arguments = invocation.ArgumentList.Arguments.Count;
                InvocationExpressionSyntax previous = LinqCalls.Receiver(invocation) as InvocationExpressionSyntax;

                if (arguments == 0 && TakesCondition.Contains(method.Name) && IsWhere(context, previous))
                {
                    Report(context, LinqCalls.NameLocation(previous), "Pass the Where condition to " + method.Name + "() and drop the Where");
                }
                else if (method.Name == "Select" && arguments == 1 && IsWhere(context, previous) && CastsTypeTested(context, previous, invocation, out ITypeSymbol tested))
                {
                    Report(context, LinqCalls.NameLocation(previous), "Use OfType<" + tested.Name + ">() instead of Where and Select");
                }

                if (method.Name == "Count" && arguments == 0 && HasCountProperty(LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken)))
                {
                    Report(context, LinqCalls.NameLocation(invocation), "Use the Count property; Count() goes through LINQ");
                }

                if (IsCopy(previous)) ReportCopy(context, previous, method.Name);
            }), SyntaxKind.InvocationExpression);
        }

        // receiver.ToList() or receiver.ToArray(), written as a member call without arguments.
        private static bool IsCopy(InvocationExpressionSyntax invocation)
        {
            if (invocation == null || invocation.ArgumentList.Arguments.Count != 0 || !(invocation.Expression is MemberAccessExpressionSyntax access)) return false;
            string name = access.Name.Identifier.ValueText;
            return name == "ToList" || name == "ToArray";
        }

        private static void ReportCopy(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax copy, string next)
        {
            if (!(context.SemanticModel.GetSymbolInfo(copy, context.CancellationToken).Symbol is IMethodSymbol)) return;
            SimpleNameSyntax name = ((MemberAccessExpressionSyntax)copy.Expression).Name;
            Report(context, name.GetLocation(), name.Identifier.ValueText + "() copies the items only to run " + next + " on them; drop it");
        }

        private static void Report(SyntaxNodeAnalysisContext context, Location location, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, message));
        }

        private static bool IsWhere(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            return invocation != null && invocation.ArgumentList.Arguments.Count == 1
                && LinqCalls.Method(context.SemanticModel, invocation, "Where", context.CancellationToken) != null;
        }

        // Where(x => x is T) followed by Select(y => (T)y).
        private static bool CastsTypeTested(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax where, InvocationExpressionSyntax select, out ITypeSymbol tested)
        {
            tested = null;
            if (!(where.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax test) || !(test.Body is BinaryExpressionSyntax isTest) || !isTest.IsKind(SyntaxKind.IsExpression)) return false;
            if (!(isTest.Left is IdentifierNameSyntax testedName) || testedName.Identifier.ValueText != test.Parameter.Identifier.ValueText) return false;
            if (!(select.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax map) || !(map.Body is CastExpressionSyntax cast)) return false;
            if (!(cast.Expression is IdentifierNameSyntax castName) || castName.Identifier.ValueText != map.Parameter.Identifier.ValueText) return false;
            tested = context.SemanticModel.GetTypeInfo(isTest.Right, context.CancellationToken).Type;
            ITypeSymbol target = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
            return tested != null && SymbolEqualityComparer.Default.Equals(tested, target);
        }

        private static bool HasCountProperty(ITypeSymbol type)
        {
            if (type == null || type is IArrayTypeSymbol) return false;
            foreach (ITypeSymbol candidate in new[] { type }.Concat(type.AllInterfaces))
            {
                for (ITypeSymbol current = candidate; current != null; current = current.BaseType)
                {
                    if (current.GetMembers("Count").OfType<IPropertySymbol>().Any(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public)) return true;
                }
            }
            return false;
        }
    }
}