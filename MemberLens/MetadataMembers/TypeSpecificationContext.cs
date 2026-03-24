using System.Collections.Generic;
using System.Reflection.Metadata;

namespace MemberLens.MetadataMembers
{
    internal class TypeSpecificationContext
    {
        public EntityHandle ResolvedEntity { get; }
        public IEnumerable<string> TypeArguments { get; }

        public TypeSpecificationContext(EntityHandle resolvedEntity, IEnumerable<string> typeArguments)
        {
            ResolvedEntity = resolvedEntity;
            TypeArguments = typeArguments;
        }
    }
}
