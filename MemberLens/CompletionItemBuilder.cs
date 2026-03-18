using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MemberLens.Attributes;
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
        private readonly SemanticModel _semanticModel;
        private readonly ImageElement _icon;

        public CompletionItemBuilder(
            INamedTypeSymbol sourceType,
            AccessorType accessorType,
            MemberAccessorCompletionSource source,
            SemanticModel semanticModel)
        {
            _sourceType = sourceType;
            _accessorType = accessorType;
            _source = source;
            _semanticModel = semanticModel;

            _icon = GetIcon();
        }

        public ImmutableArray<CompletionItem>? Build()
        {
            ImmutableArray<CompletionItem>? completionItems;
            if (_sourceType.Locations[0].IsInSource)
            {
                completionItems = GetSourceCompletionItems();
            }
            else if (_sourceType.Locations[0].IsInMetadata)
            {
                completionItems = GetMetadataCompletionItems();
            }
            else return null;

            return completionItems;
        }

        private ImmutableArray<CompletionItem>? GetSourceCompletionItems()
        {
            IEnumerable<ISymbol> sourceMembers;

            if (_accessorType == AccessorType.Field)
            {
                sourceMembers = _sourceType.GetMembers().OfType<IFieldSymbol>().Select(x => (ISymbol)x);
            }
            else if (_accessorType == AccessorType.Method)
            {
                sourceMembers = _sourceType.GetMembers().OfType<IMethodSymbol>().Select(x => (ISymbol)x);
            }
            else return null;

            var completionItems = sourceMembers
                .Where(symbol => symbol.Name != ".ctor")
                .Select(symbol =>
                {
                    var item = BuildCompletionItem(symbol.Name);

                    item.Properties.AddProperty("symbol", symbol);

                    return item;
                })
                .ToImmutableArray();

            return completionItems;
        }

        private ImmutableArray<CompletionItem>? GetMetadataCompletionItems()
        {
            //TODO: Rebuild Nuget project, add generic source class, nested source class, test match
            //TODO: Add more members: properties getter/setter
            //TODO: Test how inheritance works: protected fields, public methods, public properties
            //TODO: Interfaces: Implicit, explicit, inherited
            //TODO: open generic source type

            var compilation = _semanticModel.Compilation;

            var assembly = _sourceType.ContainingAssembly;

            if (!(compilation.GetMetadataReference(
                assembly) is PortableExecutableReference reference)) return null;

            var assemblyName = assembly.Name;
            if (assemblyName.StartsWith("System.") || assemblyName.StartsWith("Microsoft.")) return null;

            var path = reference.FilePath;
            if (path == null) return null;

            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata))
            {
                var mdReader = peReader.GetMetadataReader();

                var sourceFullName = BuildFullName(_sourceType);

                var matchFullName = string.Empty;
                var match = mdReader.TypeDefinitions
                    .Where(tdh => mdReader.GetString(mdReader.GetTypeDefinition(tdh).Name) == _sourceType.MetadataName)
                    .FirstOrDefault(tdh =>
                    {
                        matchFullName = BuildFullName(mdReader, tdh);
                        return matchFullName == sourceFullName;
                    });

                if (match.IsNil || match == default) return null;

                var key = matchFullName + _accessorType.ToString();

                if (_itemCache.TryGetValue(key, out var cachedItems))
                    return cachedItems;

                //TODO: Filter out explicit interfaces implementations -> .Contains(".")

                ImmutableArray<CompletionItem> items;
                if (_accessorType == AccessorType.Field)
                {
                    items = GetFieldItems(match, mdReader);
                }
                else if (_accessorType == AccessorType.Method)
                {
                    items = GetMethodItems(match, mdReader);
                }
                else throw new InvalidOperationException();

                _itemCache.Add(key, items);

                return items;
            }
        }

        private ImmutableArray<CompletionItem> GetFieldItems(TypeDefinitionHandle handle, MetadataReader reader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var fieldItems = typeDef.GetFields()
                .Select(fieldHandle =>
                {
                    var fieldDef = reader.GetFieldDefinition(fieldHandle);
                    var fieldName = reader.GetString(fieldDef.Name);

                    var item = BuildCompletionItem(fieldName);

                    item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromField(reader, fieldHandle));

                    return item;
                })
                .ToImmutableArray();

            return fieldItems;
        }

        private ImmutableArray<CompletionItem> GetMethodItems(TypeDefinitionHandle handle, MetadataReader reader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            return typeDef.GetMethods()
                .Select(methodHandle =>
                {
                    var methodDef = reader.GetMethodDefinition(methodHandle);
                    var methodName = reader.GetString(methodDef.Name);

                    if (methodName == ".ctor") return null;

                    var item = BuildCompletionItem(methodName);

                    item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromMethod(reader, methodHandle));

                    return item;
                })
                .Where(item => item != null)
                .ToImmutableArray();
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

        private static string BuildFullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var name = reader.GetString(typeDef.Name);

            var declaringHandle = typeDef.GetDeclaringType();
            if (!declaringHandle.IsNil)
            {
                return BuildFullName(reader, declaringHandle) + "/" + name;
            }

            var ns = reader.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string BuildFullName(INamedTypeSymbol symbol)
        {
            var name = symbol.MetadataName;

            if (symbol.ContainingType != null)
            {
                return BuildFullName(symbol.ContainingType) + "/" + name;
            }

            var cns = symbol.ContainingNamespace;
            var ns = cns != null && !cns.IsGlobalNamespace
                ? cns.ToDisplayString()
                : null;


            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
    }
}
