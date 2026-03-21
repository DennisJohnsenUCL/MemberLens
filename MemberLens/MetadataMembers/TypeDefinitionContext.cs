using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace MemberLens.MetadataMembers
{
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
