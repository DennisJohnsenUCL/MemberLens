using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using MemberLens.MetadataMembers;
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

        public bool IsEffectiveMember(TypeDefinitionContext defCtx, MethodDefinitionHandle methodHandle)
        {
            var signature = GetMethodDefinitionSignature(defCtx, methodHandle);
            return _signatures.Add(signature);
        }

        public bool IsEffectiveMember(TypeDefinitionContext defCtx, PropertyDefinitionHandle propertyHandle)
        {
            var signature = GetPropertyDefinitionSignature(defCtx, propertyHandle);
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

        public string GetMethodDefinitionSignature(TypeDefinitionContext defCtx, MethodDefinitionHandle methodHandle)
        {
            var methodDef = defCtx.MetadataReader.GetMethodDefinition(methodHandle);
            var name = defCtx.MetadataReader.GetString(methodDef.Name);
            var genericContext = new MemberSignatureGenericContext(defCtx, methodHandle);
            var provider = new MemberSignatureTypeProvider();
            var sig = methodDef.DecodeSignature(provider, genericContext);
            var parameters = string.Join(",", sig.ParameterTypes);
            return $"M:{name}({parameters})";
        }

        public string GetPropertyDefinitionSignature(TypeDefinitionContext defCtx, PropertyDefinitionHandle propHandle)
        {
            var propDef = defCtx.MetadataReader.GetPropertyDefinition(propHandle);
            var name = defCtx.MetadataReader.GetString(propDef.Name);
            var provider = new MemberSignatureTypeProvider();
            var genericContext = new MemberSignatureGenericContext(defCtx);
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
