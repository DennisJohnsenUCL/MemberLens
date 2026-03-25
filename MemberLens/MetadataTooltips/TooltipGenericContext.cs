using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class TooltipGenericContext
    {
        public string[] OriginalTypeParameters { get; }
        public string[] TypeParameters { get; }
        public string[] MethodParameters { get; }

        public TooltipGenericContext(
            IEnumerable<string> originalTypeParameters,
            IEnumerable<string> typeParameters,
            IEnumerable<string> methodParameters)
        {
            OriginalTypeParameters = originalTypeParameters.ToArray();
            TypeParameters = typeParameters.ToArray();
            MethodParameters = methodParameters.ToArray();
        }

        public static TooltipGenericContext Create(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            IEnumerable<string> typeArguments,
            MethodDefinitionHandle methodHandle = default)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var typeParams = typeDef.GetGenericParameters();
            var originalTypeNames = typeParams.Select(x => reader.GetString(reader.GetGenericParameter(x).Name));

            IEnumerable<string> typeNames = null;
            if (typeArguments != null)
            {
                typeNames = typeArguments.Select(x =>
                {
                    var index = x.LastIndexOf('.');
                    return index >= 0 ? x.Substring(index + 1) : x;
                });
            }
            else
            {
                typeNames = originalTypeNames;
            }

            IEnumerable<string> methodNames = null;
            if (!methodHandle.IsNil)
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodParams = methodDef.GetGenericParameters();
                methodNames = methodParams.Select(x => reader.GetString(reader.GetGenericParameter(x).Name));
            }

            return new TooltipGenericContext(originalTypeNames, typeNames, methodNames);
        }
    }
}
