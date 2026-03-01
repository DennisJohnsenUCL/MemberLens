using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;

namespace MemberLens
{
    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            var doc = snapshot.TextBuffer.GetRelatedDocuments().First();
            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            var semanticModel = await doc.GetSemanticModelAsync(token);

            var root = await syntaxTree.GetRootAsync(token);
            var locationToken = root.FindToken(triggerLocation.Position);
            var parent = locationToken.Parent;
            var argumentNode = parent.FirstAncestorOrSelf<ArgumentSyntax>();
            var argumentListSyntax = (ArgumentListSyntax)argumentNode.Parent;
            var expressionSyntax = argumentListSyntax.Parent;

            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            var methodSymbol = (IMethodSymbol)methodSymbolInfo.Symbol;
            var methodParameterSymbols = methodSymbol.Parameters;

            var argumentSymbolIndex = argumentListSyntax.Arguments.IndexOf(argumentNode);
            var parameterSymbol = methodParameterSymbols[argumentSymbolIndex];
            var attributes = parameterSymbol.GetAttributes();

            //Use the semantic model to get the attribute metadata instead of string comparison
            var memberAccessorAttribute = attributes.FirstOrDefault(ad => ad.AttributeClass.Name == "MemberAccessor");

            if (memberAccessorAttribute == null)
                return CompletionContext.Empty;

            var attributeProperties = memberAccessorAttribute.NamedArguments;

            //Use the metadata to get the property names and types for casting

            //Cast to int, then to enum
            var typeOfAccessor = attributeProperties.FirstOrDefault(kvp => kvp.Key == "").Value.Value;
            var typeProperty = (INamedTypeSymbol)attributeProperties.FirstOrDefault(kvp => kvp.Key == "").Value.Value;

            INamedTypeSymbol sourceType = null;

            if (typeProperty != null) sourceType = typeProperty;
            else
            {
                //Cast to int, then to enum
                var typeOfGeneric = attributeProperties.FirstOrDefault(kvp => kvp.Key == "").Value.Value;
                var indexofGeneric = (int)attributeProperties.FirstOrDefault(kvp => kvp.Key == "").Value.Value;

                //If typeofGeneric == method
                //If typeofGeneric == class
                //Assign sourceType
            }

            //Get member symbol type from typeOfAccessor
            var sourceMembers = sourceType.GetMembers(); //OfType<>

            //Better CompletionItem overloads?
            var completionItems = sourceMembers.Select(x => new CompletionItem(x.Name, this)).ToImmutableArray();
            //Better CompletionContext overloads?
            var completionContext = new CompletionContext(completionItems);
            return completionContext;
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}
