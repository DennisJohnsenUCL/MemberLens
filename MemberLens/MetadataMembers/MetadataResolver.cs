using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MemberLens.Attributes;
using Microsoft.CodeAnalysis;

namespace MemberLens.MetadataMembers
{
    internal class MetadataResolver
    {
        private readonly Compilation _compilation;
        private readonly AccessorType _accessorType;
        private readonly INamedTypeSymbol _symbol;

        public string RootKey { get; }

        public MetadataResolver(Compilation compilation, AccessorType accessorType, INamedTypeSymbol symbol)
        {
            _compilation = compilation;
            _accessorType = accessorType;
            _symbol = symbol;

            RootKey = BuildFullName(symbol) + _accessorType.ToString();
        }

        public IEnumerable<MetadataMemberInfo> GetMetadataMemberInfos()
        {
            var sourceFullName = BuildFullName(_symbol);

            var assembly = _symbol.ContainingAssembly;

            if (IsCoreLibAssembly(assembly.Name)) return null;

            if (!(_compilation.GetMetadataReference(
                assembly) is PortableExecutableReference reference)) return null;

            var path = reference.FilePath;
            if (path == null) return null;

            var stream = File.OpenRead(path);
            var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            var mdReader = peReader.GetMetadataReader();

            var match = FindTypeDefinition(mdReader, _symbol.MetadataName, sourceFullName);
            if (match == null || match.Value.IsNil || match.Value == default) return null;

            IEnumerable<MetadataMemberInfo> items;
            if (_accessorType == AccessorType.Field)
                items = GetFieldInfos(match.Value, mdReader, peReader);

            else if (_accessorType == AccessorType.Method)
                items = GetMethodInfos(match.Value, mdReader, peReader);

            else throw new InvalidOperationException();

            return items;
        }

        private IEnumerable<MetadataMemberInfo> GetFieldInfos(TypeDefinitionHandle handle, MetadataReader reader, PEReader peReader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var fieldItems = typeDef.GetFields().Where(x => !IsBackingField(reader, x));

            var completionItems = BuildFieldMemberInfos(fieldItems, reader).ToList();

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
            {
                peReader.Dispose();
                return completionItems;
            }

            return completionItems.Concat(GetInheritedFieldInfos(entity, reader, peReader));
        }

        private IEnumerable<MetadataMemberInfo> GetInheritedFieldInfos(EntityHandle entity, MetadataReader reader, PEReader peReader)
        {
            var ctx = ResolveEntityHandle(entity, reader, peReader);
            if (ctx == null) return Enumerable.Empty<MetadataMemberInfo>();
            reader = ctx.MetadataReader;

            var typeDefFields = ctx.TypeDefinition.GetFields()
                .Where(x => !IsBackingField(reader, x) && IsAccessibleFromDerived(reader, x));

            var completionItems = BuildFieldMemberInfos(typeDefFields, reader).ToList();

            var asmEntity = ctx.TypeDefinition.BaseType;

            if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
            {
                ctx.PEReader.Dispose();
                return completionItems;
            }

            return completionItems.Concat(GetInheritedFieldInfos(asmEntity, reader, ctx.PEReader));
        }

        private IEnumerable<MetadataMemberInfo> BuildFieldMemberInfos(IEnumerable<FieldDefinitionHandle> typeDefFields, MetadataReader reader)
        {
            return typeDefFields.Select(fieldHandle =>
            {
                var fieldDef = reader.GetFieldDefinition(fieldHandle);
                var fieldName = reader.GetString(fieldDef.Name);

                return new MetadataMemberInfo(fieldName, MemberDefinitionInfoFactory.FromField(reader, fieldHandle));
            });
        }

        private IEnumerable<MetadataMemberInfo> GetMethodInfos(TypeDefinitionHandle handle, MetadataReader reader, PEReader peReader)
        {
            var typeDef = reader.GetTypeDefinition(handle);

            var methodItems = typeDef.GetMethods().Where(x => !IsCtorOrExplicit(reader, x));

            var completionItems = BuildMethodMemberInfos(methodItems, reader).ToList();

            var entity = typeDef.BaseType;

            if (entity.IsNil || entity == null || entity == default)
            {
                peReader.Dispose();
                return completionItems;
            }

            return completionItems.Concat(GetInheritedMethodInfos(entity, reader, peReader));
        }

        private IEnumerable<MetadataMemberInfo> GetInheritedMethodInfos(EntityHandle entity, MetadataReader reader, PEReader peReader)
        {
            var ctx = ResolveEntityHandle(entity, reader, peReader);
            if (ctx == null) return Enumerable.Empty<MetadataMemberInfo>();
            reader = ctx.MetadataReader;

            var typeDefMethods = ctx.TypeDefinition.GetMethods()
                .Where(x => !IsCtorOrExplicit(reader, x) && IsAccessibleFromDerived(reader, x));

            var completionItems = BuildMethodMemberInfos(typeDefMethods, reader).ToList();

            var asmEntity = ctx.TypeDefinition.BaseType;

            if (asmEntity.IsNil || asmEntity == null || asmEntity == default)
            {
                ctx.PEReader.Dispose();
                return completionItems;
            }

            return completionItems.Concat(GetInheritedMethodInfos(asmEntity, reader, ctx.PEReader));
        }

        private IEnumerable<MetadataMemberInfo> BuildMethodMemberInfos(IEnumerable<MethodDefinitionHandle> typeDefMethods, MetadataReader reader)
        {
            return typeDefMethods.Select(methodHandle =>
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(methodDef.Name);

                return new MetadataMemberInfo(methodName, MemberDefinitionInfoFactory.FromMethod(reader, methodHandle));
            });
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

            var metadataRef = _compilation.References
                .OfType<PortableExecutableReference>()
                .FirstOrDefault(r =>
                {
                    var identity = _compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;
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

        private static bool IsCoreLibAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("System.") || assemblyName.StartsWith("Microsoft.");
        }

        private static bool IsBackingField(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetFieldDefinition(handle).Name);
            return name.StartsWith("<") && name.EndsWith(">k__BackingField");
        }

        private static bool IsAccessibleFromDerived(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var access = reader.GetFieldDefinition(handle).Attributes & FieldAttributes.FieldAccessMask;
            return access != FieldAttributes.Private && access != FieldAttributes.PrivateScope;
        }

        private static bool IsAccessibleFromDerived(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var access = reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
            return access != MethodAttributes.Private && access != MethodAttributes.PrivateScope;
        }

        private static bool IsCtorOrExplicit(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetMethodDefinition(handle).Name);
            return name == ".ctor" || name.Contains(".");
        }
    }
}
