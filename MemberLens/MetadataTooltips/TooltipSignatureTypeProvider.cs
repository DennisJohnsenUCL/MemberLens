using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class TooltipSignatureTypeProvider : ISignatureTypeProvider<string, TooltipGenericContext>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            switch (typeCode)
            {
                case PrimitiveTypeCode.Boolean: return "bool";
                case PrimitiveTypeCode.Byte: return "byte";
                case PrimitiveTypeCode.SByte: return "sbyte";
                case PrimitiveTypeCode.Char: return "char";
                case PrimitiveTypeCode.Int16: return "short";
                case PrimitiveTypeCode.UInt16: return "ushort";
                case PrimitiveTypeCode.Int32: return "int";
                case PrimitiveTypeCode.UInt32: return "uint";
                case PrimitiveTypeCode.Int64: return "long";
                case PrimitiveTypeCode.UInt64: return "ulong";
                case PrimitiveTypeCode.Single: return "float";
                case PrimitiveTypeCode.Double: return "double";
                case PrimitiveTypeCode.String: return "string";
                case PrimitiveTypeCode.Object: return "object";
                case PrimitiveTypeCode.IntPtr: return "nint";
                case PrimitiveTypeCode.UIntPtr: return "nuint";
                case PrimitiveTypeCode.Void: return "void";
                case PrimitiveTypeCode.TypedReference: return "TypedReference";
                default: return typeCode.ToString();
            }
        }

        public string GetTypeFromDefinition(
            MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var name = reader.GetString(typeDef.Name);

            var backtick = name.IndexOf('`');
            if (backtick > 0)
                name = name.Substring(0, backtick);

            var declaringTypeHandle = typeDef.GetDeclaringType();
            if (!declaringTypeHandle.IsNil)
            {
                var outer = GetTypeFromDefinition(reader, declaringTypeHandle, 0);
                return outer + "." + name;
            }

            var ns = reader.GetString(typeDef.Namespace);
            if (!string.IsNullOrEmpty(ns))
                return ns + "." + name;

            return name;
        }

        public string GetTypeFromReference(
            MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var ns = reader.GetString(typeRef.Namespace);

            if (ns == "System")
            {
                switch (name)
                {
                    case "Boolean": return "bool";
                    case "Byte": return "byte";
                    case "SByte": return "sbyte";
                    case "Char": return "char";
                    case "Int16": return "short";
                    case "UInt16": return "ushort";
                    case "Int32": return "int";
                    case "UInt32": return "uint";
                    case "Int64": return "long";
                    case "UInt64": return "ulong";
                    case "Single": return "float";
                    case "Double": return "double";
                    case "String": return "string";
                    case "Object": return "object";
                    case "Decimal": return "decimal";
                    case "Void": return "void";
                    case "IntPtr": return "nint";
                    case "UIntPtr": return "nuint";
                }
            }

            var backtick = name.IndexOf('`');
            if (backtick > 0)
                name = name.Substring(0, backtick);

            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var outer = GetTypeFromReference(
                    reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0);
                return outer + "." + name;
            }

            if (!string.IsNullOrEmpty(ns))
                return ns + "." + name;

            return name;
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetGenericInstantiation(
            string genericType, ImmutableArray<string> typeArguments)
        {
            if (typeArguments.Length == 1 &&
                (genericType == "System.Nullable" ||
                 genericType.EndsWith(".Nullable")))
            {
                return typeArguments[0] + "?";
            }

            return genericType + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[" + new string(',', shape.Rank - 1) + "]";
        }

        public string GetByReferenceType(string elementType)
        {
            return "ref " + elementType;
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            var parts = new System.Text.StringBuilder("delegate*<");
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                parts.Append(signature.ParameterTypes[i]);
                parts.Append(", ");
            }
            parts.Append(signature.ReturnType);
            parts.Append('>');
            return parts.ToString();
        }

        public string GetGenericMethodParameter(TooltipGenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.MethodParameters.Length)
            {
                return genericContext.MethodParameters[index];
            }

            return "!!" + index;
        }

        public string GetGenericTypeParameter(TooltipGenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.TypeParameters.Length)
            {
                return genericContext.TypeParameters[index];
            }

            return "!" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType + " pinned";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            TooltipGenericContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            var typeSpec = reader.GetTypeSpecification(handle);
            return typeSpec.DecodeSignature(this, genericContext);
        }
    }
}
