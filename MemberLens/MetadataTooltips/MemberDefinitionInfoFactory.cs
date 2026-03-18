using System.Reflection;
using System.Reflection.Metadata;

namespace MemberLens
{
    internal static class MemberDefinitionInfoFactory
    {
        public static MemberDefinitionInfo FromMethod(
            MetadataReader reader,
            MethodDefinitionHandle methodHandle)
        {
            var methodDef = reader.GetMethodDefinition(methodHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Method };

            var declaringTypeHandle = methodDef.GetDeclaringType();
            var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
            var declaringTypeName = reader.GetString(declaringType.Name);

            // Strip generic arity from declaring type name
            var backtick = declaringTypeName.IndexOf('`');
            if (backtick > 0)
                declaringTypeName = declaringTypeName.Substring(0, backtick);

            var methodName = reader.GetString(methodDef.Name);

            // Build generic context for resolving T, TResult, etc.
            var context = GenericContext.Create(reader, declaringTypeHandle, methodHandle);
            var provider = new SignatureTypeProvider(reader);
            var sig = methodDef.DecodeSignature(provider, context);

            // Modifiers
            var methodAttrs = methodDef.Attributes;

            if ((methodAttrs & MethodAttributes.Static) != 0)
                AddKeyword(info, "static");

            if ((methodAttrs & MethodAttributes.Abstract) != 0)
                AddKeyword(info, "abstract");
            else if ((methodAttrs & MethodAttributes.Final) != 0
                     && (methodAttrs & MethodAttributes.Virtual) != 0
                     && (methodAttrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "sealed override");
            else if ((methodAttrs & MethodAttributes.Virtual) != 0
                     && (methodAttrs & MethodAttributes.NewSlot) != 0)
                AddKeyword(info, "virtual");
            else if ((methodAttrs & MethodAttributes.Virtual) != 0
                     && (methodAttrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "override");

            // Return type — classify as keyword if it's a C# type keyword
            var returnType = sig.ReturnType;
            info.SignatureParts.Add(IsCSharpTypeKeyword(returnType)
                ? DisplayPart.Keyword(returnType)
                : DisplayPart.Type(returnType));
            info.SignatureParts.Add(DisplayPart.Space());

            // Declaring type (with generic params if any)
            info.SignatureParts.Add(DisplayPart.Type(declaringTypeName));
            if (context.TypeParameters.Length > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.TypeParameters.Length; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }
                    info.SignatureParts.Add(DisplayPart.TypeParameterName(context.TypeParameters[i]));
                }
                info.SignatureParts.Add(DisplayPart.Punctuation(">"));
            }

            info.SignatureParts.Add(DisplayPart.Punctuation("."));

            // Method name
            info.SignatureParts.Add(DisplayPart.MethodName(methodName));

            // Method-level generic params (e.g. <TResult>)
            if (context.MethodParameters.Length > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.MethodParameters.Length; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }
                    info.SignatureParts.Add(DisplayPart.TypeParameterName(context.MethodParameters[i]));
                }
                info.SignatureParts.Add(DisplayPart.Punctuation(">"));
            }

            // Parameters
            info.SignatureParts.Add(DisplayPart.Punctuation("("));

            var parameters = methodDef.GetParameters();
            bool first = true;
            foreach (var paramHandle in parameters)
            {
                var param = reader.GetParameter(paramHandle);

                // SequenceNumber 0 is the return-type pseudo-parameter
                if (param.SequenceNumber == 0)
                    continue;

                if (!first)
                {
                    info.SignatureParts.Add(DisplayPart.Punctuation(","));
                    info.SignatureParts.Add(DisplayPart.Space());
                }
                first = false;

                int paramIndex = param.SequenceNumber - 1;
                if (paramIndex < sig.ParameterTypes.Length)
                {
                    var paramType = sig.ParameterTypes[paramIndex];
                    // If it's a ref parameter, the type string starts with "ref "
                    // — that "ref" is a keyword, not part of the type name
                    if (paramType.StartsWith("ref "))
                    {
                        info.SignatureParts.Add(DisplayPart.Keyword("ref"));
                        info.SignatureParts.Add(DisplayPart.Space());
                        paramType = paramType.Substring(4);
                    }

                    info.SignatureParts.Add(IsCSharpTypeKeyword(paramType)
                        ? DisplayPart.Keyword(paramType)
                        : DisplayPart.Type(paramType));
                    info.SignatureParts.Add(DisplayPart.Space());
                }

                info.SignatureParts.Add(
                    DisplayPart.ParameterName(reader.GetString(param.Name)));
            }

            info.SignatureParts.Add(DisplayPart.Punctuation(")"));

            return info;
        }

        public static MemberDefinitionInfo FromField(
            MetadataReader reader,
            FieldDefinitionHandle fieldHandle)
        {
            var fieldDef = reader.GetFieldDefinition(fieldHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Field };

            var declaringTypeHandle = fieldDef.GetDeclaringType();
            var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
            var declaringTypeName = reader.GetString(declaringType.Name);

            var backtick = declaringTypeName.IndexOf('`');
            if (backtick > 0)
                declaringTypeName = declaringTypeName.Substring(0, backtick);

            var fieldName = reader.GetString(fieldDef.Name);

            var context = GenericContext.Create(reader, declaringTypeHandle);
            var provider = new SignatureTypeProvider(reader);
            var fieldType = fieldDef.DecodeSignature(provider, context);

            // Modifiers
            var fieldAttrs = fieldDef.Attributes;

            if ((fieldAttrs & FieldAttributes.Literal) != 0)
                AddKeyword(info, "const");
            else
            {
                if ((fieldAttrs & FieldAttributes.Static) != 0)
                    AddKeyword(info, "static");

                if ((fieldAttrs & FieldAttributes.InitOnly) != 0)
                    AddKeyword(info, "readonly");
            }

            // FieldType DeclaringType.FieldName
            info.SignatureParts.Add(IsCSharpTypeKeyword(fieldType)
                ? DisplayPart.Keyword(fieldType)
                : DisplayPart.Type(fieldType));
            info.SignatureParts.Add(DisplayPart.Space());

            // Declaring type (with generic params if any)
            info.SignatureParts.Add(DisplayPart.Type(declaringTypeName));
            if (context.TypeParameters.Length > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.TypeParameters.Length; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }
                    info.SignatureParts.Add(DisplayPart.TypeParameterName(context.TypeParameters[i]));
                }
                info.SignatureParts.Add(DisplayPart.Punctuation(">"));
            }

            info.SignatureParts.Add(DisplayPart.Punctuation("."));
            info.SignatureParts.Add(DisplayPart.FieldName(fieldName));

            return info;
        }

        public static MemberDefinitionInfo FromProperty(
            MetadataReader reader,
            PropertyDefinitionHandle propertyHandle)
        {
            var propertyDef = reader.GetPropertyDefinition(propertyHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Property };

            // Properties don't directly expose a declaring type — we get it
            // from the accessor method. Try getter first, then setter.
            var accessors = propertyDef.GetAccessors();
            var accessorHandle = !accessors.Getter.IsNil
                ? accessors.Getter
                : accessors.Setter;

            var accessorDef = reader.GetMethodDefinition(accessorHandle);
            var declaringTypeHandle = accessorDef.GetDeclaringType();
            var declaringType = reader.GetTypeDefinition(declaringTypeHandle);
            var declaringTypeName = reader.GetString(declaringType.Name);

            var backtick = declaringTypeName.IndexOf('`');
            if (backtick > 0)
                declaringTypeName = declaringTypeName.Substring(0, backtick);

            var propertyName = reader.GetString(propertyDef.Name);

            // Decode the property signature for the property type
            var context = GenericContext.Create(reader, declaringTypeHandle);
            var provider = new SignatureTypeProvider(reader);
            var sig = propertyDef.DecodeSignature(provider, context);

            // Modifiers — derived from the accessor method attributes
            var accessorAttrs = accessorDef.Attributes;

            if ((accessorAttrs & MethodAttributes.Static) != 0)
                AddKeyword(info, "static");

            if ((accessorAttrs & MethodAttributes.Abstract) != 0)
                AddKeyword(info, "abstract");
            else if ((accessorAttrs & MethodAttributes.Final) != 0
                     && (accessorAttrs & MethodAttributes.Virtual) != 0
                     && (accessorAttrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "sealed override");
            else if ((accessorAttrs & MethodAttributes.Virtual) != 0
                     && (accessorAttrs & MethodAttributes.NewSlot) != 0)
                AddKeyword(info, "virtual");
            else if ((accessorAttrs & MethodAttributes.Virtual) != 0
                     && (accessorAttrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "override");

            // PropertyType DeclaringType.PropertyName { get; set; }
            var propertyType = sig.ReturnType;
            info.SignatureParts.Add(IsCSharpTypeKeyword(propertyType)
                ? DisplayPart.Keyword(propertyType)
                : DisplayPart.Type(propertyType));
            info.SignatureParts.Add(DisplayPart.Space());

            // Declaring type (with generic params if any)
            info.SignatureParts.Add(DisplayPart.Type(declaringTypeName));
            if (context.TypeParameters.Length > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.TypeParameters.Length; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }
                    info.SignatureParts.Add(DisplayPart.TypeParameterName(context.TypeParameters[i]));
                }
                info.SignatureParts.Add(DisplayPart.Punctuation(">"));
            }

            info.SignatureParts.Add(DisplayPart.Punctuation("."));
            info.SignatureParts.Add(DisplayPart.PropertyName(propertyName));

            // { get; set; } accessor summary
            info.SignatureParts.Add(DisplayPart.Space());
            info.SignatureParts.Add(DisplayPart.Punctuation("{"));
            info.SignatureParts.Add(DisplayPart.Space());

            if (!accessors.Getter.IsNil)
            {
                info.SignatureParts.Add(DisplayPart.Keyword("get"));
                info.SignatureParts.Add(DisplayPart.Punctuation(";"));
                info.SignatureParts.Add(DisplayPart.Space());
            }

            if (!accessors.Setter.IsNil)
            {
                info.SignatureParts.Add(DisplayPart.Keyword("set"));
                info.SignatureParts.Add(DisplayPart.Punctuation(";"));
                info.SignatureParts.Add(DisplayPart.Space());
            }

            info.SignatureParts.Add(DisplayPart.Punctuation("}"));

            return info;
        }

        /// <summary>
        /// Appends a keyword and trailing space to the signature parts.
        /// </summary>
        private static void AddKeyword(MemberDefinitionInfo info, string keyword)
        {
            info.SignatureParts.Add(DisplayPart.Keyword(keyword));
            info.SignatureParts.Add(DisplayPart.Space());
        }

        /// <summary>
        /// Checks whether the type string is a C# keyword like int, string, etc.
        /// so we can classify it as Keyword rather than Type in the tooltip.
        /// </summary>
        internal static bool IsCSharpTypeKeyword(string typeName)
        {
            switch (typeName)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "char":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "object":
                case "void":
                case "nint":
                case "nuint":
                    return true;
                default:
                    return false;
            }
        }
    }
}
