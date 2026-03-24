using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    //TODO: Rename with specific name
    internal class SignatureTypeProvider : ISignatureTypeProvider<string, GenericContext>
    {
        private readonly MetadataReader _reader;

        public SignatureTypeProvider(MetadataReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        // ──────────────────────────────────────────────
        //  ISimpleTypeProvider
        // ──────────────────────────────────────────────

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

            // Strip the generic arity suffix (e.g. "List`1" → "List")
            var backtick = name.IndexOf('`');
            if (backtick > 0)
                name = name.Substring(0, backtick);

            // If nested, prepend the declaring type
            var declaringTypeHandle = typeDef.GetDeclaringType();
            if (!declaringTypeHandle.IsNil)
            {
                var outer = GetTypeFromDefinition(reader, declaringTypeHandle, 0);
                return outer + "." + name;
            }

            // Prepend namespace
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

            // Map well-known framework types to C# keywords
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

            // Strip generic arity suffix
            var backtick = name.IndexOf('`');
            if (backtick > 0)
                name = name.Substring(0, backtick);

            // Check if this is a nested type (resolution scope is another TypeRef)
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

        // ──────────────────────────────────────────────
        //  ISZArrayTypeProvider
        // ──────────────────────────────────────────────

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        // ──────────────────────────────────────────────
        //  IConstructedTypeProvider
        // ──────────────────────────────────────────────

        public string GetGenericInstantiation(
            string genericType, ImmutableArray<string> typeArguments)
        {
            // Handle System.Nullable`1 → T?
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
            // Multi-dimensional arrays: int[,] or int[,,]
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

        // ──────────────────────────────────────────────
        //  ISignatureTypeProvider
        // ──────────────────────────────────────────────

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            // delegate*<ParamTypes, ReturnType>
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

        public string GetGenericMethodParameter(GenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.MethodParameters.Length)
            {
                return genericContext.MethodParameters[index];
            }

            // Fallback: use the conventional !!N notation
            return "!!" + index;
        }

        public string GetGenericTypeParameter(GenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.TypeParameters.Length)
            {
                return genericContext.TypeParameters[index];
            }

            // Fallback: use the conventional !N notation
            return "!" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            // modreq/modopt are rarely shown in tooltips; just pass through
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType + " pinned";
        }

        public string GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            // TypeSpecifications are things like generic instantiations,
            // arrays, pointers, etc. The decoder will call back into us
            // recursively after decoding the blob, so we just need to
            // kick off the decode.
            var typeSpec = reader.GetTypeSpecification(handle);
            return typeSpec.DecodeSignature(this, genericContext);
        }
    }
}
