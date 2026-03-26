using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class TooltipGenericContext
    {
        public List<string> TypeArguments { get; }
        public List<string> MethodTypeParameters { get; }
        public bool SourceUnbound { get; }

        public HashSet<string> AllTypeParameterNames { get; }

        private TooltipGenericContext(
            List<string> typeArguments,
            List<string> methodTypeParameters,
            bool sourceUnbound)
        {
            TypeArguments = typeArguments;
            MethodTypeParameters = methodTypeParameters;
            SourceUnbound = sourceUnbound;

            AllTypeParameterNames = new HashSet<string>(methodTypeParameters);
            if (sourceUnbound)
                AllTypeParameterNames.UnionWith(typeArguments);
        }

        public static TooltipGenericContext Create(
            MetadataReader reader,
            IEnumerable<string> typeArguments,
            bool sourceUnbound,
            MethodDefinitionHandle methodHandle = default)
        {
            var typeArgs = typeArguments != null
                ? typeArguments.Select(x =>
                {
                    var index = x.LastIndexOf('.');
                    return index >= 0 ? x.Substring(index + 1) : x;
                }).ToList()
                : new List<string>();

            var methodTypeParams = new List<string>();
            if (!methodHandle.IsNil)
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                methodTypeParams = methodDef.GetGenericParameters()
                    .Select(x => reader.GetString(reader.GetGenericParameter(x).Name))
                    .ToList();
            }

            return new TooltipGenericContext(typeArgs, methodTypeParams, sourceUnbound);
        }
    }
}
