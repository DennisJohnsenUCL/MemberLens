using FixtureBuilder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MemberLens
{
    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            var doc = snapshot.TextBuffer.GetRelatedDocuments().FirstOrDefault();
            if (doc == null) return CompletionContext.Empty;

            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            if (syntaxTree == null) return CompletionContext.Empty;

            var root = await syntaxTree.GetRootAsync(token);

            if (token.IsCancellationRequested) return CompletionContext.Empty;

            var locationToken = root.FindToken(triggerLocation.Position);
            var tokenParent = locationToken.Parent;
            if (tokenParent == null) return CompletionContext.Empty;

            var argumentNode = tokenParent.FirstAncestorOrSelf<ArgumentSyntax>();
            if (argumentNode == null) return CompletionContext.Empty;

            var argumentListSyntax = argumentNode.Parent as ArgumentListSyntax;
            if (argumentListSyntax == null) return CompletionContext.Empty;

            var expressionSyntax = argumentListSyntax.Parent;
            if (expressionSyntax == null) return CompletionContext.Empty;

            var semanticModel = await doc.GetSemanticModelAsync(token);
            if (semanticModel == null) return CompletionContext.Empty;

            if (token.IsCancellationRequested) return CompletionContext.Empty;

            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            var methodSymbol = methodSymbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null) return CompletionContext.Empty;

            var methodParameterSymbols = methodSymbol.Parameters;

            var argumentSymbolIndex = argumentListSyntax.Arguments.IndexOf(argumentNode);
            if (argumentSymbolIndex == -1) return CompletionContext.Empty;
            if (argumentSymbolIndex >= methodParameterSymbols.Length) return CompletionContext.Empty;

            var parameterSymbol = methodParameterSymbols[argumentSymbolIndex];
            var attributes = parameterSymbol.GetAttributes();

            var memberAccessorAttribute = attributes.FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));

            if (memberAccessorAttribute == null) return CompletionContext.Empty;

            var attributeProperties = memberAccessorAttribute.NamedArguments;

            var accessorTypesConstant = attributeProperties.FirstOrDefault(kvp => kvp.Key == nameof(MemberAccessorAttribute.AccessorTypes)).Value;
            if (accessorTypesConstant.Kind == TypedConstantKind.Error) return CompletionContext.Empty;
            var accessorTypes = (AccessorTypes)(int)accessorTypesConstant.Value;

            var typeConstant = attributeProperties.FirstOrDefault(kvp => kvp.Key == nameof(MemberAccessorAttribute.Type)).Value;
            if (typeConstant.Kind == TypedConstantKind.Error) return CompletionContext.Empty;
            var type = (INamedTypeSymbol)typeConstant.Value;

            INamedTypeSymbol sourceType = null;

            if (type != null) sourceType = type;
            else
            {
                var genericSourcesConstant = attributeProperties.FirstOrDefault(kvp => kvp.Key == nameof(MemberAccessorAttribute.GenericSources)).Value;
                if (genericSourcesConstant.Kind == TypedConstantKind.Error) return CompletionContext.Empty;
                var genericSources = (GenericSources)(int)genericSourcesConstant.Value;

                var genericIndexConstant = attributeProperties.FirstOrDefault(kvp => kvp.Key == nameof(MemberAccessorAttribute.GenericIndex)).Value;
                if (genericIndexConstant.Kind == TypedConstantKind.Error) return CompletionContext.Empty;
                var genericIndex = (int)genericIndexConstant.Value;

                if (genericSources == GenericSources.Method)
                {
                    if (genericIndex >= methodSymbol.TypeArguments.Length) return CompletionContext.Empty;
                    sourceType = (INamedTypeSymbol)methodSymbol.TypeArguments[genericIndex];
                }
                else if (genericSources == GenericSources.Class)
                {
                    var sourceClass = methodSymbol.ContainingType;
                    if (genericIndex >= sourceClass.TypeArguments.Length) return CompletionContext.Empty;
                    sourceType = (INamedTypeSymbol)sourceClass.TypeArguments[genericIndex];
                }
                else return CompletionContext.Empty;
            }

            if (sourceType == null) return CompletionContext.Empty;

            ImmutableArray<ISymbol> sourceMembers;

            if (accessorTypes == AccessorTypes.Field)
            {
                sourceMembers = sourceType.GetMembers().OfType<IFieldSymbol>().Select(x => (ISymbol)x).ToImmutableArray();
            }
            else if (accessorTypes == AccessorTypes.Method)
            {
                sourceMembers = sourceType.GetMembers().OfType<IMethodSymbol>().Select(x => (ISymbol)x).ToImmutableArray();
            }
            else return CompletionContext.Empty;

            //TODO: Handle out-of-solution types

            //TODO: Filter members

            //TODO: Better CompletionItem overloads?
            var completionItems = sourceMembers.Select(x => new CompletionItem(x.Name, this)).ToImmutableArray();
            //TODO: Better CompletionContext overloads?
            var completionContext = new CompletionContext(completionItems);
            return completionContext;
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            //TODO: Implement
            return Task.FromResult<object>(string.Empty);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            //TODO: Implement
            throw new NotImplementedException();
        }
    }
}
