using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    //TODO: Rename with specific name
    internal class GenericContext
    {
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
            IEnumerable<string> typeArguments,
            MethodDefinitionHandle methodHandle = default)
        {
            ImmutableArray<string> typeNames;

            if (typeArguments != null)
            {
                typeNames = typeArguments.ToImmutableArray();
            }
            else
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                var typeParams = typeDef.GetGenericParameters();
                var typeNamesBuilder = ImmutableArray.CreateBuilder<string>(typeParams.Count);
                foreach (var gp in typeParams)
                    typeNamesBuilder.Add(reader.GetString(reader.GetGenericParameter(gp).Name));
                typeNames = typeNamesBuilder.MoveToImmutable();
            }

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

            return new GenericContext(typeNames, methodNames);
        }
    }
}
