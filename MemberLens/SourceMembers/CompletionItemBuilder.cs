using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MemberLens.Attributes;
using MemberLens.MetadataMembers;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens.SourceMembers
{
    internal class CompletionItemBuilder
    {
        private readonly static Dictionary<string, IEnumerable<CompletionItem>> _itemCache = new Dictionary<string, IEnumerable<CompletionItem>>();

        private readonly INamedTypeSymbol _sourceType;
        private readonly AccessorType _accessorType;
        private readonly MemberAccessorCompletionSource _source;

        private readonly ImageElement _icon;
        private readonly MetadataResolver _metadataResolver;
        private readonly SymbolResolver _sourceResolver;

        public CompletionItemBuilder(
            INamedTypeSymbol sourceType,
            AccessorType accessorType,
            MemberAccessorCompletionSource source,
            Compilation compilation)
        {
            _sourceType = sourceType;
            _accessorType = accessorType;
            _source = source;

            _icon = GetIcon();
            _metadataResolver = new MetadataResolver(compilation, _accessorType);
            _sourceResolver = new SymbolResolver(_accessorType, _metadataResolver);
        }

        public IEnumerable<CompletionItem> Build()
        {
            if (_sourceType.Locations[0].IsInSource)
            {
                var memberInfos = _sourceResolver.GetSourceMemberInfos(_sourceType, root: true);

                return BuildMemberCompletionItems(memberInfos);
            }

            else if (_sourceType.Locations[0].IsInMetadata)
            {
                var rootKey = _metadataResolver.GetRootKey(_sourceType);

                if (_itemCache.TryGetValue(rootKey, out var cachedItems))
                    return RebuildCachedItems(cachedItems);

                var memberInfos = _metadataResolver.GetMetadataMemberInfos(_sourceType, root: true);
                var completionItems = BuildMemberCompletionItems(memberInfos);

                if (!_itemCache.ContainsKey(rootKey))
                    _itemCache.Add(rootKey, completionItems);

                return completionItems;
            }

            else return null;
        }

        private IEnumerable<CompletionItem> BuildMemberCompletionItems(IEnumerable<SourceMemberInfo> memberInfos)
        {
            return memberInfos.Select(x =>
            {
                var item = BuildCompletionItem(x.Name);
                item.Properties.AddProperty("tooltipSource", x.TooltipSource);
                return item;
            });
        }

        private CompletionItem BuildCompletionItem(string displayName)
        {
            var fullName = $"\"{displayName}\"";

            return new CompletionItem(
                displayText: displayName,
                source: _source,
                icon: _icon,
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: string.Empty,
                insertText: fullName,
                sortText: fullName,
                filterText: fullName,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
        }

        private ImageElement GetIcon()
        {
            ImageElement icon;
            switch (_accessorType)
            {
                case AccessorType.Field:
                    icon = new ImageElement(KnownMonikers.Field.ToImageId());
                    break;
                case AccessorType.Method:
                    icon = new ImageElement(KnownMonikers.Method.ToImageId());
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return icon;
        }

        private static IEnumerable<CompletionItem> RebuildCachedItems(IEnumerable<CompletionItem> cachedItems)
        {
            return cachedItems.Select(item =>
            {
                var refreshedItem = new CompletionItem(
                    displayText: item.DisplayText,
                    source: item.Source,
                    icon: item.Icon,
                    filters: item.Filters,
                    suffix: item.Suffix,
                    insertText: item.InsertText,
                    sortText: item.SortText,
                    filterText: item.FilterText,
                    attributeIcons: item.AttributeIcons);

                var memberDef = item.Properties.GetProperty("tooltipSource");
                refreshedItem.Properties.AddProperty("tooltipSource", memberDef);

                return refreshedItem;
            });
        }
    }
}
