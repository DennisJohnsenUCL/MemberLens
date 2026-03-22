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
        private readonly MetadataResolver _resolver;

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
            _resolver = new MetadataResolver(compilation, _accessorType);
        }

        public IEnumerable<CompletionItem> Build()
        {
            if (_sourceType.Locations[0].IsInSource)
            {
                var memberInfos = GetSourceMemberInfos();

                return BuildMemberCompletionItems(memberInfos);
            }

            else if (_sourceType.Locations[0].IsInMetadata)
            {
                var rootKey = _resolver.GetRootKey(_sourceType);

                if (_itemCache.TryGetValue(rootKey, out var cachedItems))
                    return RebuildCachedItems(cachedItems);

                var memberInfos = _resolver.GetMetadataMemberInfos(_sourceType, root: true);
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

        private IEnumerable<SourceMemberInfo> GetSourceMemberInfos()
        {
            if (_accessorType == AccessorType.Field)
            {
                return GetFieldMemberInfos(_sourceType, root: true);
            }
            else if (_accessorType == AccessorType.Method)
            {
                return GetMethodMemberInfos(_sourceType, root: true);
            }
            else return null;
        }

        private IEnumerable<SourceMemberInfo> GetFieldMemberInfos(INamedTypeSymbol symbol, bool root)
        {
            var fieldMemberInfos = symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(x => !IsBackingField(x) && (root || IsAccessibleFromDerived(x)))
                .Select(x => new SourceMemberInfo(x.Name, x));

            var baseSymbol = symbol.BaseType;
            if (baseSymbol == null) return fieldMemberInfos;
            if (MemberHelper.IsCoreLibAssembly(baseSymbol.ContainingNamespace.ToDisplayString()))
                return fieldMemberInfos;

            if (baseSymbol.Locations[0].IsInSource)
                return fieldMemberInfos.Concat(GetFieldMemberInfos(baseSymbol, root: false));

            if (baseSymbol.Locations[0].IsInMetadata)
                return fieldMemberInfos.Concat(_resolver.GetMetadataMemberInfos(baseSymbol, root: false));

            return fieldMemberInfos;
        }

        private IEnumerable<SourceMemberInfo> GetMethodMemberInfos(INamedTypeSymbol symbol, bool root)
        {
            var methodMemberInfos = symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(x => !IsCtorOrExplicit(x) && (root || IsAccessibleFromDerived(x)))
                .Select(x => new SourceMemberInfo(x.Name, x));

            var baseSymbol = symbol.BaseType;
            if (baseSymbol == null) return methodMemberInfos;
            if (MemberHelper.IsCoreLibAssembly(baseSymbol.ContainingNamespace.ToDisplayString()))
                return methodMemberInfos;

            if (baseSymbol.Locations[0].IsInSource)
                return methodMemberInfos.Concat(GetMethodMemberInfos(baseSymbol, root: false));

            if (baseSymbol.Locations[0].IsInMetadata)
                return methodMemberInfos.Concat(_resolver.GetMetadataMemberInfos(baseSymbol, root: false));

            return methodMemberInfos;
        }

        private bool IsAccessibleFromDerived(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility != Accessibility.Private;
        }

        private bool IsBackingField(IFieldSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsBackingField(name);
        }

        private bool IsCtorOrExplicit(IMethodSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsCtorOrExplicit(name);
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
