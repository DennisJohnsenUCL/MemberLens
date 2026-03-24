using System.Collections.Generic;
using System.Reflection.Metadata;

namespace MemberLens.MemberSignatures
{
    internal class MemberSignatureGenericContext
    {
        public MetadataReader Reader { get; }
        public MethodDefinitionHandle MethodHandle { get; }
        public TypeDefinition TypeDefinition { get; }
        public IEnumerable<string> TypeArguments { get; }

        public MemberSignatureGenericContext() { }

        public MemberSignatureGenericContext(MetadataReader reader, MethodDefinitionHandle methodHandle, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            Reader = reader;
            MethodHandle = methodHandle;
            TypeDefinition = typeDef;
            TypeArguments = typeArguments;
        }

        public MemberSignatureGenericContext(MetadataReader reader, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            Reader = reader;
            TypeDefinition = typeDef;
            TypeArguments = typeArguments;
        }
    }
}
