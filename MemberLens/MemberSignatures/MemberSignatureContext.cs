using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;

namespace MemberLens.MemberSignatures
{
    internal class MemberSignatureContext
    {
        private readonly HashSet<string> _signatures = new HashSet<string>();

        public bool IsEffectiveMember(ISymbol symbol)
        {
            var signature = GetSymbolSignature(symbol);
            return _signatures.Add(signature);
        }

        public bool IsEffectiveMember(MetadataReader mdReader, FieldDefinitionHandle fieldHandle)
        {
            var signature = GetFieldDefinitionSignature(mdReader, fieldHandle);
            return _signatures.Add(signature);
        }

        public bool IsEffectiveMember(MetadataReader mdReader, MethodDefinitionHandle methodHandle, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            var signature = GetMethodDefinitionSignature(mdReader, methodHandle, typeDef, typeArguments);
            return _signatures.Add(signature);
        }

        public bool IsEffectiveMember(MetadataReader mdReader, PropertyDefinitionHandle propertyHandle, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            var signature = GetPropertyDefinitionSignature(mdReader, propertyHandle, typeDef, typeArguments);
            return _signatures.Add(signature);
        }

        public string GetSymbolSignature(ISymbol symbol)
        {
            switch (symbol)
            {
                case IMethodSymbol method:
                    var parameters = string.Join(",",
                        method.Parameters.Select(p =>
                            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    return $"M:{method.Name}({parameters})";

                case IPropertySymbol property:
                    if (property.IsIndexer)
                    {
                        var indexerParams = string.Join(",",
                            property.Parameters.Select(p =>
                                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        return $"P:this[{indexerParams}]";
                    }
                    return $"P:{property.Name}";

                case IFieldSymbol field:
                    return $"F:{field.Name}";

                default:
                    return $"?:{symbol.Name}";
            }
        }

        public string GetFieldDefinitionSignature(MetadataReader mdReader, FieldDefinitionHandle fieldHandle)
        {
            var fieldDef = mdReader.GetFieldDefinition(fieldHandle);
            var name = mdReader.GetString(fieldDef.Name);
            return $"F:{name}";
        }

        public string GetMethodDefinitionSignature(MetadataReader mdReader, MethodDefinitionHandle methodHandle, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            var methodDef = mdReader.GetMethodDefinition(methodHandle);
            var name = mdReader.GetString(methodDef.Name);
            var genericContext = new MemberSignatureGenericContext(mdReader, methodHandle, typeDef, typeArguments);
            var provider = new MemberSignatureTypeProvider();
            var sig = methodDef.DecodeSignature(provider, genericContext);
            var parameters = string.Join(",", sig.ParameterTypes);
            return $"M:{name}({parameters})";
        }

        public string GetPropertyDefinitionSignature(MetadataReader mdReader, PropertyDefinitionHandle propHandle, TypeDefinition typeDef, IEnumerable<string> typeArguments)
        {
            var propDef = mdReader.GetPropertyDefinition(propHandle);
            var name = mdReader.GetString(propDef.Name);
            var provider = new MemberSignatureTypeProvider();
            var genericContext = new MemberSignatureGenericContext(mdReader, default, typeDef, typeArguments);
            var sig = propDef.DecodeSignature(provider, genericContext);

            if (sig.ParameterTypes.Length > 0)
            {
                var parameters = string.Join(",", sig.ParameterTypes);
                return $"P:this[{parameters}]";
            }

            return $"P:{name}";
        }
    }
}
