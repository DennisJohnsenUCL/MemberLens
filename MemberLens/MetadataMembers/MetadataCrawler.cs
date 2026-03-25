using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MemberLens.MemberSignatures;
using MemberLens.SourceMembers;
using Microsoft.CodeAnalysis;

namespace MemberLens.MetadataMembers
{
    internal class MetadataCrawler
    {
        private readonly Compilation _compilation;

        public MetadataCrawler(Compilation compilation)
        {
            _compilation = compilation;
        }

        public TypeDefinitionContext GetBaseType(TypeDefinitionContext defCtx)
        {
            var baseEntity = defCtx.TypeDefinition.BaseType;

            if (baseEntity == null || baseEntity.IsNil || baseEntity == default)
            {
                defCtx.PEReader.Dispose();
                return null;
            }

            return ResolveEntityHandle(baseEntity, defCtx);
        }

        private TypeDefinitionContext ResolveEntityHandle(EntityHandle entity, TypeDefinitionContext defCtx)
        {
            TypeDefinitionContext NoContext()
            {
                defCtx.PEReader.Dispose();
                return null;
            }

            IEnumerable<string> typeArguments = null;
            if (entity.Kind == HandleKind.TypeSpecification)
            {
                if (!(ResolveTypeSpecification((TypeSpecificationHandle)entity, defCtx) is TypeSpecificationContext specCtx))
                    return NoContext();

                entity = specCtx.ResolvedEntity;
                typeArguments = specCtx.TypeArguments;
            }

            if (entity.Kind == HandleKind.TypeDefinition)
            {
                var typeDefHandle = (TypeDefinitionHandle)entity;
                var typeDef = defCtx.MetadataReader.GetTypeDefinition(typeDefHandle);
                return new TypeDefinitionContext(typeDef, defCtx.PEReader, defCtx.MetadataReader, typeArguments);
            }

            else if (entity.Kind == HandleKind.TypeReference)
            {
                var typeRefHandle = (TypeReferenceHandle)entity;
                var fromRefCtx = ResolveTypeReference(typeRefHandle, defCtx.MetadataReader, defCtx.PEReader, typeArguments);
                if (fromRefCtx == null) return null;
                return fromRefCtx;
            }
            else return NoContext();
        }

        private static TypeSpecificationContext ResolveTypeSpecification(TypeSpecificationHandle typeSpecHandle, TypeDefinitionContext defCtx)
        {
            var typeSpec = defCtx.MetadataReader.GetTypeSpecification(typeSpecHandle);

            var blobReader = defCtx.MetadataReader.GetBlobReader(typeSpec.Signature);
            var signatureTypeCode = blobReader.ReadSignatureTypeCode();

            if (signatureTypeCode == SignatureTypeCode.GenericTypeInstance)
            {
                blobReader.ReadSignatureTypeCode();
                var typeHandle = blobReader.ReadTypeHandle();

                if (typeHandle == null || typeHandle.IsNil || typeHandle == default)
                    return null;

                var provider = new MemberSignatureTypeProvider();
                var genericContext = new MemberSignatureGenericContext(defCtx);
                typeSpec.DecodeSignature(provider, genericContext);
                var typeArguments = provider.LastTypeArguments;

                return new TypeSpecificationContext(typeHandle, typeArguments);
            }
            else return null;
        }

        private TypeDefinitionContext ResolveTypeReference(TypeReferenceHandle typeRefHandle, MetadataReader mdReader, PEReader peReader, IEnumerable<string> typeArguments)
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

            if (MemberHelper.IsCoreLibAssembly(asmName)) return NoContext();

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

            var match = MetadataHelper.FindTypeDefinition(extMdReader, sourceName, sourceFullName);

            if (match == null || match.Value.IsNil || match.Value == default)
            {
                extPeReader.Dispose();
                return NoContext();
            }

            peReader.Dispose();

            var extTypeDef = extMdReader.GetTypeDefinition(match.Value);
            return new TypeDefinitionContext(extTypeDef, extPeReader, extMdReader, typeArguments);
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
    }
}
