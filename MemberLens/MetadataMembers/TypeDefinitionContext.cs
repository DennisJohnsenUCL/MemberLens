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
        public IEnumerable<string> TypeArguments { get; }

        public TypeDefinitionContext(
            TypeDefinition typeDefinition,
            PEReader pEReader,
            MetadataReader metadataReader,
            IEnumerable<string> typeArguments)
        {
            TypeDefinition = typeDefinition;
            PEReader = pEReader;
            MetadataReader = metadataReader;
            TypeArguments = typeArguments;
        }
    }
}
