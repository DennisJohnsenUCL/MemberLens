using System.Collections.Generic;
using System.Reflection.Metadata;
using MemberLens.MetadataMembers;

namespace MemberLens.MemberSignatures
{
    internal class MemberSignatureGenericContext
    {
        public MetadataReader Reader { get; }
        public MethodDefinitionHandle MethodHandle { get; }
        public TypeDefinition TypeDefinition { get; }
        public IEnumerable<string> TypeArguments { get; }

        public MemberSignatureGenericContext(TypeDefinitionContext defCtx, MethodDefinitionHandle methodHandle)
            : this(defCtx)
        {
            MethodHandle = methodHandle;
        }

        public MemberSignatureGenericContext(TypeDefinitionContext defCtx)
        {
            Reader = defCtx.MetadataReader;
            TypeDefinition = defCtx.TypeDefinition;
            TypeArguments = defCtx.TypeArguments;
        }
    }
}
