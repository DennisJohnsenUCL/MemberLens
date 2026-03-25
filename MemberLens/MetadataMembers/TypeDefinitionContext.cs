using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MemberLens.MetadataMembers
{
    internal class TypeDefinitionContext
    {
        public TypeDefinition TypeDefinition { get; }
        public PEReader PEReader { get; }
        public MetadataReader MetadataReader { get; }
        public IEnumerable<string> TypeArguments { get; set; }

        public TypeDefinitionContext(
            TypeDefinition typeDefinition,
            PEReader pEReader,
            MetadataReader metadataReader,
            IEnumerable<string> typeArguments) : this(typeDefinition, pEReader, metadataReader)
        {
            TypeArguments = typeArguments;
        }

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
