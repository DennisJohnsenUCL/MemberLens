using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
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
                sourceMembers = GetEffectiveMembers(_sourceType).OfType<IFieldSymbol>().Select(x => (ISymbol)x);
            }
            else if (_accessorType == AccessorType.Method)
            {
                sourceMembers = GetEffectiveMembers(_sourceType)
                    .OfType<IMethodSymbol>()
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

        private ImmutableArray<CompletionItem>? GetMetadataCompletionItems()
        {
            var compilation = _semanticModel.Compilation;

            var assembly = _sourceType.ContainingAssembly;

            var assemblyName = assembly.Name;
            if (assemblyName.StartsWith("System.") || assemblyName.StartsWith("Microsoft.")) return null;

            if (!(compilation.GetMetadataReference(
                assembly) is PortableExecutableReference reference)) return null;

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
                    return RebuildCachedItems(cachedItems);

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

                if (!_itemCache.ContainsKey(key))
                    _itemCache.Add(key, items);

                return items;
            }
        }

        private ImmutableArray<CompletionItem> GetFieldItems(TypeDefinitionHandle handle, MetadataReader reader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var fieldItems = typeDef.GetFields().Where(x =>
            {
                var field = reader.GetFieldDefinition(x);

                var name = reader.GetString(field.Name);
                if (name.StartsWith("<") && name.EndsWith(">k__BackingField"))
                    return false;

                return true;
            }).ToList();

            var completionItems = fieldItems.Select(fieldHandle =>
            {
                var fieldDef = reader.GetFieldDefinition(fieldHandle);
                var fieldName = reader.GetString(fieldDef.Name);

                var item = BuildCompletionItem(fieldName);

                item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromField(reader, fieldHandle));

                return item;
            });

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
                return completionItems.ToImmutableArray();

            return completionItems.Concat(GetInheritedFieldItems(entity, reader)).ToImmutableArray();
        }

        private ImmutableArray<CompletionItem> GetInheritedFieldItems(EntityHandle entity, MetadataReader reader)
        {
            if (entity.Kind == HandleKind.TypeSpecification)
            {
                var typeSpecHandle = (TypeSpecificationHandle)entity;
                var typeSpec = reader.GetTypeSpecification(typeSpecHandle);

                var blobReader = reader.GetBlobReader(typeSpec.Signature);
                var signatureTypeCode = blobReader.ReadSignatureTypeCode();

                if (signatureTypeCode == SignatureTypeCode.GenericTypeInstance)
                {
                    blobReader.ReadSignatureTypeCode();
                    var typeHandle = blobReader.ReadTypeHandle();

                    if (typeHandle == null || typeHandle.IsNil || typeHandle == default)
                        return ImmutableArray<CompletionItem>.Empty;

                    entity = typeHandle;
                }
                else return ImmutableArray<CompletionItem>.Empty;
            }

            if (entity.Kind == HandleKind.TypeDefinition)
            {
                var typeDefHandle = (TypeDefinitionHandle)entity;
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                var typeDefFields = typeDef.GetFields().Where(x =>
                {
                    var field = reader.GetFieldDefinition(x);

                    var name = reader.GetString(field.Name);
                    if (name.StartsWith("<") && name.EndsWith(">k__BackingField"))
                        return false;

                    var access = field.Attributes & FieldAttributes.FieldAccessMask;

                    return access != FieldAttributes.Private && access != FieldAttributes.PrivateScope;
                });

                var completionItems = typeDefFields.Select(fieldHandle =>
                {
                    var fieldDef = reader.GetFieldDefinition(fieldHandle);
                    var fieldName = reader.GetString(fieldDef.Name);

                    var item = BuildCompletionItem(fieldName);

                    item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromField(reader, fieldHandle));

                    return item;
                });

                var asmEntity = typeDef.BaseType;

                if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
                    return completionItems.ToImmutableArray();

                return completionItems.Concat(GetInheritedFieldItems(asmEntity, reader)).ToImmutableArray();
            }

            else if (entity.Kind == HandleKind.TypeReference)
            {
                var typeRefHandle = (TypeReferenceHandle)entity;
                var typeRef = reader.GetTypeReference(typeRefHandle);
                var resScope = typeRef.ResolutionScope;
                if (resScope.Kind != HandleKind.AssemblyReference) return ImmutableArray<CompletionItem>.Empty;

                var asmRefHandle = (AssemblyReferenceHandle)resScope;
                var asmRef = reader.GetAssemblyReference(asmRefHandle);
                var asmName = reader.GetString(asmRef.Name);

                if (asmName.StartsWith("System.") || asmName.StartsWith("Microsoft.")) return ImmutableArray<CompletionItem>.Empty;

                var compilation = _semanticModel.Compilation;

                var metadataRef = compilation.References
                    .OfType<PortableExecutableReference>()
                    .FirstOrDefault(r =>
                    {
                        var identity = compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;
                        return identity?.Name == asmName;
                    });

                if (metadataRef?.FilePath == null) return ImmutableArray<CompletionItem>.Empty;

                using (var stream = File.OpenRead(metadataRef.FilePath))
                using (var peReader = new PEReader(stream))
                {
                    var extReader = peReader.GetMetadataReader();

                    var sourceFullName = BuildFullName(reader, typeRefHandle);

                    var matchFullName = string.Empty;
                    var match = extReader.TypeDefinitions
                        .Where(tdh => extReader.GetString(extReader.GetTypeDefinition(tdh).Name) == reader.GetString(typeRef.Name))
                        .FirstOrDefault(tdh =>
                        {
                            matchFullName = BuildFullName(extReader, tdh);
                            return matchFullName == sourceFullName;
                        });

                    if (match.IsNil || match == default) return ImmutableArray<CompletionItem>.Empty;

                    var extTypeDef = extReader.GetTypeDefinition(match);

                    var extTypeFields = extTypeDef.GetFields().Where(x =>
                    {
                        var field = extReader.GetFieldDefinition(x);
                        var access = field.Attributes & FieldAttributes.FieldAccessMask;

                        var name = extReader.GetString(field.Name);

                        if (name.StartsWith("<") && name.EndsWith(">k__BackingField"))
                            return false;

                        return access != FieldAttributes.Private && access != FieldAttributes.PrivateScope;
                    });

                    var completionItems = extTypeFields.Select(fieldHandle =>
                    {
                        var fieldDef = extReader.GetFieldDefinition(fieldHandle);
                        var fieldName = extReader.GetString(fieldDef.Name);

                        var item = BuildCompletionItem(fieldName);

                        item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromField(extReader, fieldHandle));

                        return item;
                    });

                    var extEntity = extTypeDef.BaseType;

                    if (extEntity.IsNil || extEntity == null || extEntity == default)
                        return completionItems.ToImmutableArray();

                    return completionItems.Concat(GetInheritedFieldItems(extEntity, extReader)).ToImmutableArray();
                }
            }
            else return ImmutableArray<CompletionItem>.Empty;
        }

        private ImmutableArray<CompletionItem> GetMethodItems(TypeDefinitionHandle handle, MetadataReader reader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var methodItems = typeDef.GetMethods().Where(x =>
            {
                var method = reader.GetMethodDefinition(x);

                var name = reader.GetString(method.Name);
                if (name == ".ctor" || name.Contains(".")) return false;

                return true;
            }).ToList();

            var completionItems = methodItems.Select(methodHandle =>
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(methodDef.Name);

                var item = BuildCompletionItem(methodName);

                item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromMethod(reader, methodHandle));

                return item;
            });

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
                return completionItems.ToImmutableArray();

            return completionItems.Concat(GetInheritedMethodItems(entity, reader)).ToImmutableArray();

        }

        private ImmutableArray<CompletionItem> GetInheritedMethodItems(EntityHandle entity, MetadataReader reader)
        {
            if (entity.Kind == HandleKind.TypeSpecification)
            {
                var typeSpecHandle = (TypeSpecificationHandle)entity;
                var typeSpec = reader.GetTypeSpecification(typeSpecHandle);

                var blobReader = reader.GetBlobReader(typeSpec.Signature);
                var signatureTypeCode = blobReader.ReadSignatureTypeCode();

                if (signatureTypeCode == SignatureTypeCode.GenericTypeInstance)
                {
                    blobReader.ReadSignatureTypeCode();
                    var typeHandle = blobReader.ReadTypeHandle();

                    if (typeHandle == null || typeHandle.IsNil || typeHandle == default)
                        return ImmutableArray<CompletionItem>.Empty;

                    entity = typeHandle;
                }
                else return ImmutableArray<CompletionItem>.Empty;
            }

            if (entity.Kind == HandleKind.TypeDefinition)
            {
                var typeDefHandle = (TypeDefinitionHandle)entity;
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                var typeDefMethods = typeDef.GetMethods().Where(x =>
                {
                    var method = reader.GetMethodDefinition(x);

                    var name = reader.GetString(method.Name);
                    if (name == ".ctor" || name.Contains(".")) return false;

                    var access = method.Attributes & MethodAttributes.MemberAccessMask;

                    return access != MethodAttributes.Private && access != MethodAttributes.PrivateScope;
                });

                var completionItems = typeDefMethods.Select(methodHandle =>
                {
                    var methodDef = reader.GetMethodDefinition(methodHandle);
                    var methodName = reader.GetString(methodDef.Name);

                    var item = BuildCompletionItem(methodName);

                    item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromMethod(reader, methodHandle));

                    return item;
                });

                var asmEntity = typeDef.BaseType;

                if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
                    return completionItems.ToImmutableArray();

                return completionItems.Concat(GetInheritedMethodItems(asmEntity, reader)).ToImmutableArray();
            }

            else if (entity.Kind == HandleKind.TypeReference)
            {
                var typeRefHandle = (TypeReferenceHandle)entity;
                var typeRef = reader.GetTypeReference(typeRefHandle);
                var resScope = typeRef.ResolutionScope;
                if (resScope.Kind != HandleKind.AssemblyReference) return ImmutableArray<CompletionItem>.Empty;

                var asmRefHandle = (AssemblyReferenceHandle)resScope;
                var asmRef = reader.GetAssemblyReference(asmRefHandle);
                var asmName = reader.GetString(asmRef.Name);

                if (asmName.StartsWith("System.") || asmName.StartsWith("Microsoft.")) return ImmutableArray<CompletionItem>.Empty;

                var compilation = _semanticModel.Compilation;

                var metadataRef = compilation.References
                    .OfType<PortableExecutableReference>()
                    .FirstOrDefault(r =>
                    {
                        var identity = compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;
                        return identity?.Name == asmName;
                    });

                if (metadataRef?.FilePath == null) return ImmutableArray<CompletionItem>.Empty;

                using (var stream = File.OpenRead(metadataRef.FilePath))
                using (var peReader = new PEReader(stream))
                {
                    var extReader = peReader.GetMetadataReader();

                    var sourceFullName = BuildFullName(reader, typeRefHandle);

                    var matchFullName = string.Empty;
                    var match = extReader.TypeDefinitions
                        .Where(tdh => extReader.GetString(extReader.GetTypeDefinition(tdh).Name) == reader.GetString(typeRef.Name))
                        .FirstOrDefault(tdh =>
                        {
                            matchFullName = BuildFullName(extReader, tdh);
                            return matchFullName == sourceFullName;
                        });

                    if (match.IsNil || match == default) return ImmutableArray<CompletionItem>.Empty;

                    var extTypeDef = extReader.GetTypeDefinition(match);

                    var extTypeMethods = extTypeDef.GetMethods().Where(x =>
                    {
                        var method = extReader.GetMethodDefinition(x);
                        var access = method.Attributes & MethodAttributes.MemberAccessMask;

                        var name = extReader.GetString(method.Name);
                        if (name == ".ctor" || name.Contains(".")) return false;

                        return access != MethodAttributes.Private && access != MethodAttributes.PrivateScope;
                    });

                    var completionItems = extTypeMethods.Select(methodHandle =>
                    {
                        var methodDef = extReader.GetMethodDefinition(methodHandle);
                        var methodName = extReader.GetString(methodDef.Name);

                        var item = BuildCompletionItem(methodName);

                        item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromMethod(extReader, methodHandle));

                        return item;
                    });

                    var extEntity = extTypeDef.BaseType;

                    if (extEntity.IsNil || extEntity == null || extEntity == default)
                        return completionItems.ToImmutableArray();

                    return completionItems.Concat(GetInheritedMethodItems(extEntity, extReader)).ToImmutableArray();
                }
            }
            else return ImmutableArray<CompletionItem>.Empty;
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

        private static string BuildFullName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);

            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return BuildFullName(reader, (TypeReferenceHandle)typeRef.ResolutionScope) + "/" + name;
            }

            var ns = reader.GetString(typeRef.Namespace);
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
                    attributeIcons: ImmutableArray<ImageElement>.Empty);

                var memberDef = item.Properties.GetProperty("memberDef");
                refreshedItem.Properties.AddProperty("memberDef", memberDef);

                return refreshedItem;
            }).ToImmutableArray();
        }

        //TODO: Move everything related to this (below here) elsewhere
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
