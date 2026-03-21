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

namespace MemberLens
{
    internal class CompletionItemBuilder
    {
        private readonly static Dictionary<string, ImmutableArray<CompletionItem>> _itemCache = new Dictionary<string, ImmutableArray<CompletionItem>>();

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
            _resolver = new MetadataResolver(compilation, _accessorType, _sourceType);
        }

        public ImmutableArray<CompletionItem>? Build()
        {
            ImmutableArray<CompletionItem>? completionItems;
            if (_sourceType.Locations[0].IsInSource)
                completionItems = GetSourceCompletionItems();

            //TODO: Maybe own method
            else if (_sourceType.Locations[0].IsInMetadata)
            {
                if (_itemCache.TryGetValue(_resolver.RootKey, out var cachedItems))
                    return RebuildCachedItems(cachedItems);

                var metadataMemberInfos = _resolver.GetMetadataMemberInfos();
                completionItems = metadataMemberInfos.Select(x =>
                {
                    var item = BuildCompletionItem(x.Name);
                    item.Properties.AddProperty("memberDef", x.MemberDefinitionInfo);
                    return item;
                }).ToImmutableArray();

                if (!_itemCache.ContainsKey(_resolver.RootKey))
                    _itemCache.Add(_resolver.RootKey, completionItems.Value);
            }

            else return null;

            return completionItems;
        }

        private ImmutableArray<CompletionItem>? GetSourceCompletionItems()
        {
            IEnumerable<ISymbol> sourceMembers;

            if (_accessorType == AccessorType.Field)
            {
                sourceMembers = GetEffectiveMembers(_sourceType).OfType<IFieldSymbol>().Select(x => (ISymbol)x);
            }
            else if (_accessorType == AccessorType.Method)
            {
                sourceMembers = GetEffectiveMembers(_sourceType)
                    .OfType<IMethodSymbol>()
                    //TODO: Deduplicate this
                    .Where(symbol => symbol.Name != ".ctor" && !symbol.Name.Contains("."))
                    .Select(x => (ISymbol)x);
            }
            else return null;

            var completionItems = sourceMembers
                .Select(symbol =>
                {
                    var item = BuildCompletionItem(symbol.Name);

                    item.Properties.AddProperty("symbol", symbol);

                    return item;
                })
                .ToImmutableArray();

            return completionItems;
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

        private static ImmutableArray<CompletionItem> RebuildCachedItems(ImmutableArray<CompletionItem> cachedItems)
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

                var memberDef = item.Properties.GetProperty("memberDef");
                refreshedItem.Properties.AddProperty("memberDef", memberDef);

                return refreshedItem;
            }).ToImmutableArray();
        }

        //TODO: Move everything related to this (below here) elsewhere
        //TODO: This skips object, should stop as soon as anything in System or Microsoft
        public static IEnumerable<ISymbol> GetEffectiveMembers(INamedTypeSymbol type)
        {
            var emittedSignatures = new HashSet<string>();

            var current = type;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                foreach (var member in current.GetMembers())
                {
                    if (IsObjectMember(member))
                        continue;

                    if (member.IsImplicitlyDeclared)
                        continue;

                    var sig = GetMemberSignature(member);

                    if (!emittedSignatures.Add(sig))
                        continue;

                    yield return member;
                }

                current = current.BaseType;
            }
        }

        private static string GetMemberSignature(ISymbol symbol)
        {
            switch (symbol)
            {
                case IMethodSymbol method:
                    var parameters = string.Join(",",
                        method.Parameters.Select(p =>
                            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    return $"M:{method.Name}({parameters})";

                case IPropertySymbol property:
                    if (property.IsIndexer)
                    {
                        var indexerParams = string.Join(",",
                            property.Parameters.Select(p =>
                                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        return $"P:this[{indexerParams}]";
                    }
                    return $"P:{property.Name}";

                case IFieldSymbol field:
                    return $"F:{field.Name}";

                case IEventSymbol evt:
                    return $"E:{evt.Name}";

                case INamedTypeSymbol type:
                    return $"T:{type.Name}+{(type).Arity}";

                default:
                    return $"?:{symbol.Name}";
            }
        }

        private static bool IsObjectMember(ISymbol symbol)
        {
            if (symbol is IMethodSymbol method)
            {
                var root = method;
                while (root.OverriddenMethod != null)
                    root = root.OverriddenMethod;
                return root.ContainingType.SpecialType == SpecialType.System_Object;
            }

            return symbol.ContainingType.SpecialType == SpecialType.System_Object;
        }
    }
}
