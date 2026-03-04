using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens
{
    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            var doc = snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (doc == null) return CompletionContext.Empty;

            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            if (syntaxTree == null) return CompletionContext.Empty;

            var root = await syntaxTree.GetRootAsync(token);

            if (token.IsCancellationRequested) return CompletionContext.Empty;

            var position = triggerLocation.Position;
            var locationToken = root.FindToken(position);
            var tokenParent = locationToken.Parent;
            if (tokenParent == null) return CompletionContext.Empty;

            var argumentListSyntax = tokenParent.FirstAncestorOrSelf<ArgumentListSyntax>();
            if (argumentListSyntax == null) return CompletionContext.Empty;

            var expressionSyntax = argumentListSyntax.Parent;
            if (expressionSyntax == null) return CompletionContext.Empty;

            int argumentSymbolIndex = argumentListSyntax.Arguments.GetSeparators().Count(separator => separator.SpanStart < position);

            var semanticModel = await doc.GetSemanticModelAsync(token);
            if (semanticModel == null) return CompletionContext.Empty;

            if (token.IsCancellationRequested) return CompletionContext.Empty;

            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            var methodSymbol = methodSymbolInfo.Symbol as IMethodSymbol;

            AttributeData memberAccessorAttribute = null;
            if (methodSymbol == null)
            {
                var candidateSymbols = methodSymbolInfo.CandidateSymbols;
                if (candidateSymbols.Length == 0) return CompletionContext.Empty;

                foreach (var candidateSymbol in candidateSymbols.Cast<IMethodSymbol>())
                {
                    var methodParameterSymbols = candidateSymbol.Parameters;
                    if (argumentSymbolIndex >= methodParameterSymbols.Length) continue;

                    var parameterSymbol = methodParameterSymbols[argumentSymbolIndex];
                    if (parameterSymbol.Type.Name != "String") continue;

                    var attributes = parameterSymbol.GetAttributes();

                    memberAccessorAttribute = attributes.FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));
                    if (memberAccessorAttribute == null) continue;

                    methodSymbol = candidateSymbol;
                    break;
                }
            }
            else
            {
                var methodParameterSymbols = methodSymbol.Parameters;
                if (argumentSymbolIndex >= methodParameterSymbols.Length) return CompletionContext.Empty;

                var parameterSymbol = methodParameterSymbols[argumentSymbolIndex];
                if (parameterSymbol.Type.Name != "String") return CompletionContext.Empty;

                var attributes = parameterSymbol.GetAttributes();

                memberAccessorAttribute = attributes.FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));
                if (memberAccessorAttribute == null) return CompletionContext.Empty;
            }

            if (methodSymbol == null) return CompletionContext.Empty;

            var constructorArgs = memberAccessorAttribute.ConstructorArguments;
            if (constructorArgs.Length < 2) return CompletionContext.Empty;
            var accessorTypes = (AccessorTypes)(int)constructorArgs[0].Value;

            INamedTypeSymbol sourceType = null;

            if (constructorArgs[1].Kind == TypedConstantKind.Type)
            {
                sourceType = (INamedTypeSymbol)constructorArgs[1].Value;
            }
            else if (constructorArgs.Length >= 3)
            {
                var genericSources = (GenericSources)(int)constructorArgs[1].Value;
                var genericIndex = (int)constructorArgs[2].Value;

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
            else return CompletionContext.Empty;

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
            //TODO: Filter away BCL and others
            //Handle inherited methods and fields
            //TODO: Filter away based on typing?
            //TODO: Handle initial position of menu
            //TODO: DisplayText: ClassName.Member?

            ImageElement icon;
            switch (accessorTypes)
            {
                case AccessorTypes.Field:
                    icon = new ImageElement(KnownMonikers.Field.ToImageId());
                    break;
                case AccessorTypes.Method:
                    icon = new ImageElement(KnownMonikers.Method.ToImageId());
                    break;
                default:
                    throw new InvalidOperationException();
            }

            var completionItems = sourceMembers
                .Where(symbol => symbol.Name != ".ctor")
                .Select(symbol =>
                {
                    var item = new CompletionItem($"\"{symbol.Name}\"", this, icon);
                    item.Properties.AddProperty("symbol", symbol);
                    return item;
                })
                .ToImmutableArray();

            var completionContext = new CompletionContext(completionItems);
            return completionContext;
        }

        public async Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!(item.Properties.GetProperty("symbol") is ISymbol symbol))
                return Task.FromResult<object>(string.Empty);

            //TODO: Go To Definition?
            return SymbolTooltipBuilder.Build(symbol, token);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            if (!snapshot.TextBuffer.GetRelatedDocuments().Any())
                return CompletionStartData.DoesNotParticipateInCompletion;

            var position = triggerLocation.Position;

            if (position == snapshot.Length || position - 1 < 0)
                return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));

            var initial = snapshot[position - 1];
            if (char.IsLetterOrDigit(initial) || initial == '_' || initial == '"')
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

                var snapshotSpan = new SnapshotSpan(snapshot, start, end - start);
                return new CompletionStartData(CompletionParticipation.ProvidesItems, snapshotSpan);
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));
        }
    }
}
