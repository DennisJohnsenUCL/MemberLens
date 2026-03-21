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
                completionItems = GetSourceCompletionItems();

            else if (_sourceType.Locations[0].IsInMetadata)
                completionItems = GetMetadataCompletionItems();

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

            if (IsCoreLibAssembly(assembly.Name)) return null;

            if (!(compilation.GetMetadataReference(
                assembly) is PortableExecutableReference reference)) return null;

            var path = reference.FilePath;
            if (path == null) return null;

            var stream = File.OpenRead(path);
            var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            var mdReader = peReader.GetMetadataReader();

            var sourceFullName = BuildFullName(_sourceType);

            var match = FindTypeDefinition(mdReader, _sourceType.MetadataName, sourceFullName);
            if (match == null || match.Value.IsNil || match.Value == default) return null;

            var key = BuildFullName(mdReader, match.Value) + _accessorType.ToString();

            if (_itemCache.TryGetValue(key, out var cachedItems))
                return RebuildCachedItems(cachedItems);

            ImmutableArray<CompletionItem> items;
            if (_accessorType == AccessorType.Field)
                items = GetFieldItems(match.Value, mdReader, peReader);

            else if (_accessorType == AccessorType.Method)
                items = GetMethodItems(match.Value, mdReader, peReader);

            else throw new InvalidOperationException();

            if (!_itemCache.ContainsKey(key))
                _itemCache.Add(key, items);

            return items;
        }

        private TypeDefinitionContext ResolveEntityHandle(EntityHandle entity, MetadataReader mdReader, PEReader peReader)
        {
            TypeDefinitionContext NoContext()
            {
                peReader.Dispose();
                return null;
            }

            if (entity.Kind == HandleKind.TypeSpecification)
            {
                if (!(ResolveTypeSpecification((TypeSpecificationHandle)entity, mdReader) is EntityHandle resolved))
                    return NoContext();
                entity = resolved;
            }

            if (entity.Kind == HandleKind.TypeDefinition)
            {
                var typeDefHandle = (TypeDefinitionHandle)entity;
                var typeDef = mdReader.GetTypeDefinition(typeDefHandle);
                return new TypeDefinitionContext(typeDef, peReader, mdReader);
            }

            else if (entity.Kind == HandleKind.TypeReference)
            {
                var typeRefHandle = (TypeReferenceHandle)entity;
                return ResolveTypeReference(typeRefHandle, mdReader, peReader);
            }
            else return NoContext();
        }

        private TypeDefinitionContext ResolveTypeReference(TypeReferenceHandle typeRefHandle, MetadataReader mdReader, PEReader peReader)
        {
            TypeDefinitionContext NoContext()
            {
                peReader.Dispose();
                return null;
            }

            var typeRef = mdReader.GetTypeReference(typeRefHandle);
            var resScope = typeRef.ResolutionScope;
            if (resScope.Kind != HandleKind.AssemblyReference) return NoContext();

            var asmRefHandle = (AssemblyReferenceHandle)resScope;
            var asmRef = mdReader.GetAssemblyReference(asmRefHandle);
            var asmName = mdReader.GetString(asmRef.Name);

            if (IsCoreLibAssembly(asmName)) return NoContext();

            var compilation = _semanticModel.Compilation;

            var metadataRef = compilation.References
                .OfType<PortableExecutableReference>()
                .FirstOrDefault(r =>
                {
                    var identity = compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;
                    return identity?.Name == asmName;
                });

            if (metadataRef?.FilePath == null) return NoContext();

            var stream = File.OpenRead(metadataRef.FilePath);
            var extPeReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            var extMdReader = extPeReader.GetMetadataReader();

            var sourceName = mdReader.GetString(typeRef.Name);
            var sourceFullName = BuildFullName(mdReader, typeRefHandle);

            var match = FindTypeDefinition(extMdReader, sourceName, sourceFullName);

            if (match == null || match.Value.IsNil || match.Value == default)
            {
                extPeReader.Dispose();
                return NoContext();
            }

            peReader.Dispose();

            var extTypeDef = extMdReader.GetTypeDefinition(match.Value);
            return new TypeDefinitionContext(extTypeDef, extPeReader, extMdReader);
        }

        private static bool IsCoreLibAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("System.") || assemblyName.StartsWith("Microsoft.");
        }

        private static bool IsBackingField(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetFieldDefinition(handle).Name);
            return name.StartsWith("<") && name.EndsWith(">k__BackingField");
        }

        public static bool IsAccessibleFromDerived(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var access = reader.GetFieldDefinition(handle).Attributes & FieldAttributes.FieldAccessMask;
            return access != FieldAttributes.Private && access != FieldAttributes.PrivateScope;
        }

        public static bool IsAccessibleFromDerived(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var access = reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
            return access != MethodAttributes.Private && access != MethodAttributes.PrivateScope;
        }

        public static bool IsCtorOrExplicit(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetMethodDefinition(handle).Name);
            return name == ".ctor" || name.Contains(".");
        }

        private ImmutableArray<CompletionItem> GetFieldItems(TypeDefinitionHandle handle, MetadataReader reader, PEReader peReader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var fieldItems = typeDef.GetFields().Where(x => !IsBackingField(reader, x));

            var completionItems = BuildFieldCompletionItems(fieldItems, reader).ToList();

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
            {
                peReader.Dispose();
                return completionItems.ToImmutableArray();
            }

            return completionItems.Concat(GetInheritedFieldItems(entity, reader, peReader)).ToImmutableArray();
        }

        private ImmutableArray<CompletionItem> GetInheritedFieldItems(EntityHandle entity, MetadataReader reader, PEReader peReader)
        {
            var ctx = ResolveEntityHandle(entity, reader, peReader);
            if (ctx == null) return ImmutableArray<CompletionItem>.Empty;
            reader = ctx.MetadataReader;

            var typeDefFields = ctx.TypeDefinition.GetFields()
                .Where(x => !IsBackingField(reader, x) && IsAccessibleFromDerived(reader, x));

            var completionItems = BuildFieldCompletionItems(typeDefFields, reader).ToList();

            var asmEntity = ctx.TypeDefinition.BaseType;

            if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
            {
                ctx.PEReader.Dispose();
                return completionItems.ToImmutableArray();
            }

            return completionItems.Concat(GetInheritedFieldItems(asmEntity, reader, ctx.PEReader)).ToImmutableArray();
        }

        private ImmutableArray<CompletionItem> GetMethodItems(TypeDefinitionHandle handle, MetadataReader reader, PEReader peReader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var methodItems = typeDef.GetMethods().Where(x => !IsCtorOrExplicit(reader, x));

            var completionItems = BuildMethodCompletionItems(methodItems, reader).ToList();

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
            {
                peReader.Dispose();
                return completionItems.ToImmutableArray();
            }

            return completionItems.Concat(GetInheritedMethodItems(entity, reader, peReader)).ToImmutableArray();
        }

        private static EntityHandle? ResolveTypeSpecification(TypeSpecificationHandle typeSpecHandle, MetadataReader reader)
        {
            var typeSpec = reader.GetTypeSpecification(typeSpecHandle);

            var blobReader = reader.GetBlobReader(typeSpec.Signature);
            var signatureTypeCode = blobReader.ReadSignatureTypeCode();

            if (signatureTypeCode == SignatureTypeCode.GenericTypeInstance)
            {
                blobReader.ReadSignatureTypeCode();
                var typeHandle = blobReader.ReadTypeHandle();

                if (typeHandle == null || typeHandle.IsNil || typeHandle == default)
                    return null;

                return typeHandle;
            }
            else return null;
        }

        private IEnumerable<CompletionItem> BuildFieldCompletionItems(IEnumerable<FieldDefinitionHandle> typeDefFields, MetadataReader reader)
        {
            return typeDefFields.Select(fieldHandle =>
            {
                var fieldDef = reader.GetFieldDefinition(fieldHandle);
                var fieldName = reader.GetString(fieldDef.Name);

                var item = BuildCompletionItem(fieldName);

                item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromField(reader, fieldHandle));

                return item;
            });
        }

        private IEnumerable<CompletionItem> BuildMethodCompletionItems(IEnumerable<MethodDefinitionHandle> typeDefMethods, MetadataReader reader)
        {
            return typeDefMethods.Select(methodHandle =>
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(methodDef.Name);

                var item = BuildCompletionItem(methodName);

                item.Properties.AddProperty("memberDef", MemberDefinitionInfoFactory.FromMethod(reader, methodHandle));

                return item;
            });
        }

        private ImmutableArray<CompletionItem> GetInheritedMethodItems(EntityHandle entity, MetadataReader reader, PEReader peReader)
        {
            var ctx = ResolveEntityHandle(entity, reader, peReader);
            if (ctx == null) return ImmutableArray<CompletionItem>.Empty;
            reader = ctx.MetadataReader;

            var typeDefMethods = ctx.TypeDefinition.GetMethods()
                .Where(x => !IsCtorOrExplicit(reader, x) && IsAccessibleFromDerived(reader, x));

            var completionItems = BuildMethodCompletionItems(typeDefMethods, reader).ToList();

            var asmEntity = ctx.TypeDefinition.BaseType;

            if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
            {
                ctx.PEReader.Dispose();
                return completionItems.ToImmutableArray();
            }

            return completionItems.Concat(GetInheritedMethodItems(asmEntity, reader, ctx.PEReader)).ToImmutableArray();
        }

        private static TypeDefinitionHandle? FindTypeDefinition(MetadataReader reader, string name, string fullName)
        {
            foreach (var tdh in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(tdh);
                if (reader.GetString(td.Name) != name)
                    continue;

                if (BuildFullName(reader, tdh) == fullName)
                    return tdh;
            }

            return null;
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

    internal class TypeDefinitionContext
    {
        public TypeDefinition TypeDefinition { get; }
        public PEReader PEReader { get; }
        public MetadataReader MetadataReader { get; }

        public TypeDefinitionContext(
            TypeDefinition typeDefinition,
            PEReader pEReader,
            MetadataReader metadataReader)
        {
            TypeDefinition = typeDefinition;
            PEReader = pEReader;
            MetadataReader = metadataReader;
        }
    }
}
