using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal static class MemberDefinitionInfoFactory
    {
        public static MemberDefinitionInfo FromMethod(
            MetadataReader reader,
            MethodDefinitionHandle methodHandle,
            IEnumerable<string> typeArguments)
        {
            var methodDef = reader.GetMethodDefinition(methodHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Method };

            var declaringTypeHandle = methodDef.GetDeclaringType();
            var declaringTypeName = GetDeclaringTypeName(reader, declaringTypeHandle);
            var methodName = reader.GetString(methodDef.Name);

            var context = GenericContext.Create(reader, declaringTypeHandle, typeArguments, methodHandle);
            var provider = new SignatureTypeProvider(reader);
            var sig = methodDef.DecodeSignature(provider, context);

            info.TypeParameterNames = BuildTypeParameterNames(context);

            AddMethodModifiers(info, methodDef.Attributes);

            AddTypePart(info, sig.ReturnType);

            AddDeclaringType(info, declaringTypeName, context);

            info.SignatureParts.Add(DisplayPart.MethodName(methodName));

            AddMethodTypeParameters(info, context);

            AddMethodParameters(info, reader, methodDef, sig);

            return info;
        }

        public static MemberDefinitionInfo FromField(
            MetadataReader reader,
            FieldDefinitionHandle fieldHandle)
        {
            var fieldDef = reader.GetFieldDefinition(fieldHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Field };

            var declaringTypeHandle = fieldDef.GetDeclaringType();
            var declaringTypeName = GetDeclaringTypeName(reader, declaringTypeHandle);
            var fieldName = reader.GetString(fieldDef.Name);

            var context = GenericContext.Create(reader, declaringTypeHandle, null);
            var provider = new SignatureTypeProvider(reader);
            var fieldType = fieldDef.DecodeSignature(provider, context);

            info.TypeParameterNames = BuildTypeParameterNames(context);

            AddFieldModifiers(info, fieldDef);

            AddTypePart(info, fieldType);

            AddDeclaringType(info, declaringTypeName, context);

            info.SignatureParts.Add(DisplayPart.FieldName(fieldName));

            return info;
        }

        public static MemberDefinitionInfo FromProperty(
            MetadataReader reader,
            PropertyDefinitionHandle propertyHandle)
        {
            var propertyDef = reader.GetPropertyDefinition(propertyHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Property };

            var accessors = propertyDef.GetAccessors();
            var accessorHandle = !accessors.Getter.IsNil
                ? accessors.Getter
                : accessors.Setter;

            var accessorDef = reader.GetMethodDefinition(accessorHandle);

            var declaringTypeHandle = accessorDef.GetDeclaringType();
            var declaringTypeName = GetDeclaringTypeName(reader, declaringTypeHandle);
            var propertyName = reader.GetString(propertyDef.Name);

            var context = GenericContext.Create(reader, declaringTypeHandle, null);
            var provider = new SignatureTypeProvider(reader);
            var sig = propertyDef.DecodeSignature(provider, context);

            info.TypeParameterNames = BuildTypeParameterNames(context);

            AddMethodModifiers(info, accessorDef.Attributes);

            AddTypePart(info, sig.ReturnType);

            AddDeclaringType(info, declaringTypeName, context);

            info.SignatureParts.Add(DisplayPart.PropertyName(propertyName));

            AddPropertyAccessorSummary(info, accessors);

            return info;
        }

        private static string GetDeclaringTypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var declaringType = reader.GetTypeDefinition(handle);
            var declaringTypeName = reader.GetString(declaringType.Name);

            var backtick = declaringTypeName.IndexOf('`');
            if (backtick > 0)
                declaringTypeName = declaringTypeName.Substring(0, backtick);

            return declaringTypeName;
        }

        private static HashSet<string> BuildTypeParameterNames(GenericContext context)
        {
            var names = new HashSet<string>();
            foreach (var tp in context.TypeParameters)
                names.Add(tp);
            foreach (var mp in context.MethodParameters)
                names.Add(mp);
            return names;
        }

        private static void AddDeclaringType(MemberDefinitionInfo info, string declaringTypeName, GenericContext context)
        {
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
        }

        private static void AddMethodModifiers(MemberDefinitionInfo info, MethodAttributes attrs)
        {
            if ((attrs & MethodAttributes.Static) != 0)
                AddKeyword(info, "static");

            if ((attrs & MethodAttributes.Abstract) != 0)
                AddKeyword(info, "abstract");
            else if ((attrs & MethodAttributes.Final) != 0
                     && (attrs & MethodAttributes.Virtual) != 0
                     && (attrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "sealed override");
            else if ((attrs & MethodAttributes.Virtual) != 0
                     && (attrs & MethodAttributes.NewSlot) != 0)
                AddKeyword(info, "virtual");
            else if ((attrs & MethodAttributes.Virtual) != 0
                     && (attrs & MethodAttributes.NewSlot) == 0)
                AddKeyword(info, "override");
        }

        private static void AddTypePart(MemberDefinitionInfo info, string typeName)
        {
            info.SignatureParts.Add(MetadataTooltipHelper.IsCSharpTypeKeyword(typeName)
                ? DisplayPart.Keyword(typeName)
                : DisplayPart.Type(typeName));
            info.SignatureParts.Add(DisplayPart.Space());
        }

        private static void AddKeyword(MemberDefinitionInfo info, string keyword)
        {
            info.SignatureParts.Add(DisplayPart.Keyword(keyword));
            info.SignatureParts.Add(DisplayPart.Space());
        }

        private static void AddMethodTypeParameters(MemberDefinitionInfo info, GenericContext context)
        {
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
        }

        private static void AddMethodParameters(MemberDefinitionInfo info, MetadataReader reader, MethodDefinition methodDef, MethodSignature<string> sig)
        {
            info.SignatureParts.Add(DisplayPart.Punctuation("("));

            var parameters = methodDef.GetParameters();
            bool first = true;
            foreach (var paramHandle in parameters)
            {
                var param = reader.GetParameter(paramHandle);

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

                    if (paramType.StartsWith("ref "))
                    {
                        bool isIn = (param.Attributes & System.Reflection.ParameterAttributes.In) != 0;
                        bool isOut = (param.Attributes & System.Reflection.ParameterAttributes.Out) != 0;

                        if (isOut)
                        {
                            info.SignatureParts.Add(DisplayPart.Keyword("out"));
                        }
                        else if (isIn)
                        {
                            bool isRefReadonly = HasAttribute(reader, param.GetCustomAttributes(),
                                "System.Runtime.CompilerServices", "RequiresLocationAttribute");

                            if (isRefReadonly)
                            {
                                info.SignatureParts.Add(DisplayPart.Keyword("ref readonly"));
                            }
                            else
                            {
                                info.SignatureParts.Add(DisplayPart.Keyword("in"));
                            }
                        }
                        else
                        {
                            info.SignatureParts.Add(DisplayPart.Keyword("ref"));
                        }

                        info.SignatureParts.Add(DisplayPart.Space());
                        paramType = paramType.Substring(4);
                    }

                    AddTypePart(info, paramType);
                }

                info.SignatureParts.Add(
                    DisplayPart.ParameterName(reader.GetString(param.Name)));
            }

            info.SignatureParts.Add(DisplayPart.Punctuation(")"));
        }

        private static void AddFieldModifiers(MemberDefinitionInfo info, FieldDefinition fieldDef)
        {
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
        }

        private static void AddPropertyAccessorSummary(MemberDefinitionInfo info, PropertyAccessors accessors)
        {
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
        }

        private static bool HasAttribute(
            MetadataReader reader,
            CustomAttributeHandleCollection attributes,
            string namespaceName,
            string typeName)
        {
            foreach (var attrHandle in attributes)
            {
                var attr = reader.GetCustomAttribute(attrHandle);
                if (attr.Constructor.Kind == HandleKind.MemberReference)
                {
                    var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    if (ctor.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                        if (reader.GetString(typeRef.Namespace) == namespaceName
                            && reader.GetString(typeRef.Name) == typeName)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
