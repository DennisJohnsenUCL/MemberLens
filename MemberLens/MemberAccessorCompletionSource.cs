using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemberLens.Attributes;
using MemberLens.MetadataMembers;
using MemberLens.MetadataTooltips;
using MemberLens.SourceMembers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;

namespace MemberLens
{
    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        private readonly CompletionContext Empty = CompletionContext.Empty;

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var symCtx = await CompletionSourceHelper.GetSymbolContextAsync(triggerLocation, token);
            if (symCtx == null) return Empty;

            var symbolAndAttribute = CompletionSourceHelper.GetSymbolAndAttribute(symCtx.MethodSymbolInfo, symCtx.ArgumentIndex);
            if (symbolAndAttribute == null) return Empty;
            var (methodSymbol, memberAccessorAttribute) = symbolAndAttribute.Value;

            var constructorArgs = memberAccessorAttribute.ConstructorArguments;
            if (constructorArgs.Length < 2) return Empty;

            var sourceType = CompletionSourceHelper.GetSourceType(methodSymbol, constructorArgs);
            if (sourceType == null) return Empty;

            var accessorType = (AccessorType)(int)constructorArgs[0].Value;

            var metadataCrawler = new MetadataCrawler(symCtx.Compilation);
            var metadataResolver = new MetadataResolver(metadataCrawler, symCtx.Compilation, accessorType);
            var symbolResolver = new SymbolResolver(accessorType, metadataResolver);

            using (var completionItemBuilder = new CompletionItemBuilder(metadataResolver, symbolResolver, sourceType, accessorType, this))
            {
                var completionItems = completionItemBuilder.Build();
                if (completionItems == null) return Empty;

                var completionContext = new CompletionContext(completionItems.ToImmutableArray());
                return completionContext;
            }
        }

        public async Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!item.Properties.TryGetProperty("tooltipSource", out object tooltipSource))
                return string.Empty;

            if (tooltipSource is ISymbol symbol)
                return SymbolTooltipBuilder.Build(symbol, token);

            if (tooltipSource is MemberDefinitionInfo memberDef)
                return MetadataTooltipBuilder.Build(memberDef);

            return string.Empty;
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
                var snapshotSpan = CompletionSourceHelper.GetSnapshotSpan(snapshot, position);
                return new CompletionStartData(CompletionParticipation.ProvidesItems, snapshotSpan);
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));
        }
    }
}
