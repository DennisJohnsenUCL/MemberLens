using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

        public TypeDefinitionContext GetBaseType(TypeDefinitionContext context)
        {
            var baseEntity = context.TypeDefinition.BaseType;

            if (baseEntity == null || baseEntity.IsNil || baseEntity == default)
            {
                context.PEReader.Dispose();
                return null;
            }

            return ResolveEntityHandle(baseEntity, context.MetadataReader, context.PEReader);
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

            if (MetadataHelper.IsCoreLibAssembly(asmName)) return NoContext();

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
            return new TypeDefinitionContext(extTypeDef, extPeReader, extMdReader);
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
