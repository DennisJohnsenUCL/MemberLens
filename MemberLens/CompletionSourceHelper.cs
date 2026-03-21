using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemberLens.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;

namespace MemberLens
{
    internal class CompletionSourceHelper
    {
        internal static async Task<CompletionSymbolContext> GetSymbolContextAsync(SnapshotPoint triggerLocation, CancellationToken token = default)
        {
            var doc = triggerLocation.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (doc == null) return null;

            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            if (syntaxTree == null) return null;

            var root = await syntaxTree.GetRootAsync(token);

            if (token.IsCancellationRequested) return null;

            var position = triggerLocation.Position;
            var tokenParent = root.FindToken(position).Parent;
            if (tokenParent == null) return null;

            var argumentListSyntax = tokenParent.FirstAncestorOrSelf<ArgumentListSyntax>();
            if (argumentListSyntax == null) return null;

            var expressionSyntax = argumentListSyntax.Parent;
            if (expressionSyntax == null) return null;

            var semanticModel = await doc.GetSemanticModelAsync(token);
            if (semanticModel == null) return null;

            if (token.IsCancellationRequested) return null;

            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            int argumentIndex = argumentListSyntax.Arguments.GetSeparators().Count(separator => separator.SpanStart < position);

            return new CompletionSymbolContext(methodSymbolInfo, argumentIndex, semanticModel.Compilation);
        }

        internal static (IMethodSymbol MethodSymbol, AttributeData MemberAccessorAttribute)? GetSymbolAndAttribute(
            SymbolInfo methodSymbolInfo, int argumentSymbolIndex)
        {
            var candidates = methodSymbolInfo.Symbol is IMethodSymbol directMatch
                ? new[] { directMatch }
                : methodSymbolInfo.CandidateSymbols.Cast<IMethodSymbol>();

            foreach (var candidate in candidates)
            {
                if (argumentSymbolIndex >= candidate.Parameters.Length) continue;

                var parameterSymbol = candidate.Parameters[argumentSymbolIndex];
                if (parameterSymbol.Type.Name != "String") continue;

                var memberAccessorAttribute = parameterSymbol.GetAttributes()
                    .FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));
                if (memberAccessorAttribute == null) continue;

                return (candidate, memberAccessorAttribute);
            }

            return null;
        }

        internal static INamedTypeSymbol GetSourceType(IMethodSymbol methodSymbol, ImmutableArray<TypedConstant> constructorArgs)
        {
            INamedTypeSymbol sourceType;
            if (constructorArgs[1].Kind == TypedConstantKind.Type)
            {
                sourceType = (INamedTypeSymbol)constructorArgs[1].Value;
            }
            else if (constructorArgs.Length == 3)
            {
                var genericSources = (GenericSource)(int)constructorArgs[1].Value;
                var genericIndex = (int)constructorArgs[2].Value;

                if (genericSources == GenericSource.Method)
                {
                    if (genericIndex >= methodSymbol.TypeArguments.Length) return null;
                    sourceType = (INamedTypeSymbol)methodSymbol.TypeArguments[genericIndex];
                }
                else if (genericSources == GenericSource.Class)
                {
                    var sourceClass = methodSymbol.ContainingType;
                    if (genericIndex >= sourceClass.TypeArguments.Length) return null;
                    sourceType = (INamedTypeSymbol)sourceClass.TypeArguments[genericIndex];
                }
                else return null;
            }
            else return null;

            if (sourceType.IsUnboundGenericType) sourceType = sourceType.OriginalDefinition;

            return sourceType;
        }

        internal static SnapshotSpan GetSnapshotSpan(ITextSnapshot snapshot, int position)
        {
            var start = position - 1;
            while (true)
            {
                if (start <= 0) break;
                var c = snapshot[start - 1];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '"') break;
                else
                {
                    start--;
                }
            }

            var end = position;
            while (true)
            {
                if (end >= snapshot.Length) break;
                var c = snapshot[end];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '"') break;
                else
                {
                    end++;
                }
            }

            return new SnapshotSpan(snapshot, start, end - start);
        }
    }
}
