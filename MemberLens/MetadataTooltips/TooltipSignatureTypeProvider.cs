using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class TooltipSignatureTypeProvider : ISignatureTypeProvider<ImmutableArray<DisplayPart>, TooltipGenericContext>
    {
        private static ImmutableArray<DisplayPart> KeywordPart(string keyword) =>
            ImmutableArray.Create(DisplayPart.Keyword(keyword));

        private static ImmutableArray<DisplayPart> TypePart(string name) =>
            ImmutableArray.Create(DisplayPart.Type(name));

        private static ImmutableArray<DisplayPart> ClassifyTypeName(string name)
        {
            if (MetadataTooltipHelper.IsCSharpTypeKeyword(name))
                return KeywordPart(name);

            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
                name = name.Substring(lastDot + 1);

            return TypePart(name);
        }

        public ImmutableArray<DisplayPart> GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            switch (typeCode)
            {
                case PrimitiveTypeCode.Boolean: return KeywordPart("bool");
                case PrimitiveTypeCode.Byte: return KeywordPart("byte");
                case PrimitiveTypeCode.SByte: return KeywordPart("sbyte");
                case PrimitiveTypeCode.Char: return KeywordPart("char");
                case PrimitiveTypeCode.Int16: return KeywordPart("short");
                case PrimitiveTypeCode.UInt16: return KeywordPart("ushort");
                case PrimitiveTypeCode.Int32: return KeywordPart("int");
                case PrimitiveTypeCode.UInt32: return KeywordPart("uint");
                case PrimitiveTypeCode.Int64: return KeywordPart("long");
                case PrimitiveTypeCode.UInt64: return KeywordPart("ulong");
                case PrimitiveTypeCode.Single: return KeywordPart("float");
                case PrimitiveTypeCode.Double: return KeywordPart("double");
                case PrimitiveTypeCode.String: return KeywordPart("string");
                case PrimitiveTypeCode.Object: return KeywordPart("object");
                case PrimitiveTypeCode.IntPtr: return KeywordPart("nint");
                case PrimitiveTypeCode.UIntPtr: return KeywordPart("nuint");
                case PrimitiveTypeCode.Void: return KeywordPart("void");
                case PrimitiveTypeCode.TypedReference: return TypePart("TypedReference");
                default: return TypePart(typeCode.ToString());
            }
        }

        public ImmutableArray<DisplayPart> GetTypeFromDefinition(
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
                return outer.Add(DisplayPart.Punctuation(".")).Add(DisplayPart.Type(name));
            }

            return TypePart(name);
        }

        public ImmutableArray<DisplayPart> GetTypeFromReference(
            MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var ns = reader.GetString(typeRef.Namespace);

            if (ns == "System")
            {
                switch (name)
                {
                    case "Boolean": return KeywordPart("bool");
                    case "Byte": return KeywordPart("byte");
                    case "SByte": return KeywordPart("sbyte");
                    case "Char": return KeywordPart("char");
                    case "Int16": return KeywordPart("short");
                    case "UInt16": return KeywordPart("ushort");
                    case "Int32": return KeywordPart("int");
                    case "UInt32": return KeywordPart("uint");
                    case "Int64": return KeywordPart("long");
                    case "UInt64": return KeywordPart("ulong");
                    case "Single": return KeywordPart("float");
                    case "Double": return KeywordPart("double");
                    case "String": return KeywordPart("string");
                    case "Object": return KeywordPart("object");
                    case "Decimal": return KeywordPart("decimal");
                    case "Void": return KeywordPart("void");
                    case "IntPtr": return KeywordPart("nint");
                    case "UIntPtr": return KeywordPart("nuint");
                }
            }

            var backtick = name.IndexOf('`');
            if (backtick > 0)
                name = name.Substring(0, backtick);

            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var outer = GetTypeFromReference(
                    reader, (TypeReferenceHandle)typeRef.ResolutionScope, 0);
                return outer.Add(DisplayPart.Punctuation(".")).Add(DisplayPart.Type(name));
            }

            return TypePart(name);
        }

        public ImmutableArray<DisplayPart> GetSZArrayType(ImmutableArray<DisplayPart> elementType)
        {
            return elementType.Add(DisplayPart.Punctuation("[]"));
        }

        public ImmutableArray<DisplayPart> GetGenericInstantiation(
            ImmutableArray<DisplayPart> genericType,
            ImmutableArray<ImmutableArray<DisplayPart>> typeArguments)
        {
            if (typeArguments.Length == 1 && IsNullableType(genericType))
            {
                return typeArguments[0].Add(DisplayPart.Punctuation("?"));
            }

            var builder = ImmutableArray.CreateBuilder<DisplayPart>();
            builder.AddRange(genericType);
            builder.Add(DisplayPart.Punctuation("<"));

            for (int i = 0; i < typeArguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Add(DisplayPart.Punctuation(","));
                    builder.Add(DisplayPart.Space());
                }
                builder.AddRange(typeArguments[i]);
            }

            builder.Add(DisplayPart.Punctuation(">"));
            return builder.ToImmutable();
        }

        public ImmutableArray<DisplayPart> GetArrayType(
            ImmutableArray<DisplayPart> elementType, ArrayShape shape)
        {
            return elementType.Add(
                DisplayPart.Punctuation("[" + new string(',', shape.Rank - 1) + "]"));
        }

        public ImmutableArray<DisplayPart> GetByReferenceType(ImmutableArray<DisplayPart> elementType)
        {
            return ImmutableArray.Create(
                DisplayPart.Keyword("ref"),
                DisplayPart.Space()
            ).AddRange(elementType);
        }

        public ImmutableArray<DisplayPart> GetPointerType(ImmutableArray<DisplayPart> elementType)
        {
            return elementType.Add(DisplayPart.Punctuation("*"));
        }

        public ImmutableArray<DisplayPart> GetFunctionPointerType(
            MethodSignature<ImmutableArray<DisplayPart>> signature)
        {
            var builder = ImmutableArray.CreateBuilder<DisplayPart>();
            builder.Add(DisplayPart.Keyword("delegate"));
            builder.Add(DisplayPart.Punctuation("*<"));

            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                builder.AddRange(signature.ParameterTypes[i]);
                builder.Add(DisplayPart.Punctuation(","));
                builder.Add(DisplayPart.Space());
            }

            builder.AddRange(signature.ReturnType);
            builder.Add(DisplayPart.Punctuation(">"));
            return builder.ToImmutable();
        }

        public ImmutableArray<DisplayPart> GetGenericMethodParameter(
            TooltipGenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.MethodTypeParameters.Count)
            {
                return ImmutableArray.Create(
                    DisplayPart.TypeParameterName(genericContext.MethodTypeParameters[index]));
            }

            return TypePart("!!" + index);
        }

        public ImmutableArray<DisplayPart> GetGenericTypeParameter(
            TooltipGenericContext genericContext, int index)
        {
            if (genericContext != null &&
                index < genericContext.TypeArguments.Count)
            {
                var name = genericContext.TypeArguments[index];

                if (genericContext.AllTypeParameterNames.Contains(name))
                    return ImmutableArray.Create(DisplayPart.TypeParameterName(name));

                return ClassifyTypeName(name);
            }

            return TypePart("!" + index);
        }

        public ImmutableArray<DisplayPart> GetModifiedType(
            ImmutableArray<DisplayPart> modifier,
            ImmutableArray<DisplayPart> unmodifiedType,
            bool isRequired)
        {
            return unmodifiedType;
        }

        public ImmutableArray<DisplayPart> GetPinnedType(ImmutableArray<DisplayPart> elementType)
        {
            return elementType
                .Add(DisplayPart.Space())
                .Add(DisplayPart.Keyword("pinned"));
        }

        public ImmutableArray<DisplayPart> GetTypeFromSpecification(
            MetadataReader reader,
            TooltipGenericContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            var typeSpec = reader.GetTypeSpecification(handle);
            return typeSpec.DecodeSignature(this, genericContext);
        }

        private static bool IsNullableType(ImmutableArray<DisplayPart> genericType)
        {
            if (genericType.Length == 1)
            {
                var text = genericType[0].Text;
                return text == "Nullable" || text.EndsWith(".Nullable");
            }
            return false;
        }
    }
}
