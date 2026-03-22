using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class GenericContext
    {
        public static readonly GenericContext Empty = new GenericContext(
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);

        public ImmutableArray<string> TypeParameters { get; }
        public ImmutableArray<string> MethodParameters { get; }

        public GenericContext(
            ImmutableArray<string> typeParameters,
            ImmutableArray<string> methodParameters)
        {
            TypeParameters = typeParameters;
            MethodParameters = methodParameters;
        }

        public static GenericContext Create(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            MethodDefinitionHandle methodHandle = default)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeParams = typeDef.GetGenericParameters();
            var typeNames = ImmutableArray.CreateBuilder<string>(typeParams.Count);
            foreach (var gp in typeParams)
                typeNames.Add(reader.GetString(reader.GetGenericParameter(gp).Name));

            var methodNames = ImmutableArray<string>.Empty;
            if (!methodHandle.IsNil)
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodParams = methodDef.GetGenericParameters();
                var builder = ImmutableArray.CreateBuilder<string>(methodParams.Count);
                foreach (var gp in methodParams)
                    builder.Add(reader.GetString(reader.GetGenericParameter(gp).Name));
                methodNames = builder.MoveToImmutable();
            }

            return new GenericContext(typeNames.MoveToImmutable(), methodNames);
        }
    }
}
