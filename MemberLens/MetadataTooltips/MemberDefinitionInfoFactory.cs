using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace MemberLens.MetadataTooltips
{
    internal class MemberDefinitionInfoFactory
    {
        private readonly bool _sourceUnbound;

        public MemberDefinitionInfoFactory(bool sourceUnbound)
        {
            _sourceUnbound = sourceUnbound;
        }

        public MemberDefinitionInfo FromMethod(
            MetadataReader reader,
            MethodDefinitionHandle methodHandle,
            IEnumerable<string> typeArguments)
        {
            var methodDef = reader.GetMethodDefinition(methodHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Method };

            var declaringTypeHandle = methodDef.GetDeclaringType();
            var declaringTypeName = GetDeclaringTypeName(reader, declaringTypeHandle);
            var methodName = reader.GetString(methodDef.Name);

            var context = TooltipGenericContext.Create(reader, typeArguments, _sourceUnbound, methodHandle);
            var provider = new TooltipSignatureTypeProvider();
            var sig = methodDef.DecodeSignature(provider, context);

            AddMethodModifiers(info, methodDef.Attributes);

            AddTypeParts(info, sig.ReturnType);
            info.SignatureParts.Add(DisplayPart.Space());

            AddDeclaringType(info, declaringTypeName, context);

            info.SignatureParts.Add(DisplayPart.MethodName(methodName));

            AddMethodTypeParameters(info, context);

            AddMethodParameters(info, reader, methodDef, sig);

            return info;
        }

        public MemberDefinitionInfo FromField(
            MetadataReader reader,
            FieldDefinitionHandle fieldHandle,
            IEnumerable<string> typeArguments)
        {
            var fieldDef = reader.GetFieldDefinition(fieldHandle);
            var info = new MemberDefinitionInfo { Kind = MemberKind.Field };

            var declaringTypeHandle = fieldDef.GetDeclaringType();
            var declaringTypeName = GetDeclaringTypeName(reader, declaringTypeHandle);
            var fieldName = reader.GetString(fieldDef.Name);

            var context = TooltipGenericContext.Create(reader, typeArguments, _sourceUnbound);
            var provider = new TooltipSignatureTypeProvider();
            var fieldType = fieldDef.DecodeSignature(provider, context);

            AddFieldModifiers(info, fieldDef);

            AddTypeParts(info, fieldType);
            info.SignatureParts.Add(DisplayPart.Space());

            AddDeclaringType(info, declaringTypeName, context);

            info.SignatureParts.Add(DisplayPart.FieldName(fieldName));

            return info;
        }

        public MemberDefinitionInfo FromProperty(
            MetadataReader reader,
            PropertyDefinitionHandle propertyHandle,
            IEnumerable<string> typeArguments)
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

            var context = TooltipGenericContext.Create(reader, typeArguments, _sourceUnbound);
            var provider = new TooltipSignatureTypeProvider();
            var sig = propertyDef.DecodeSignature(provider, context);

            AddMethodModifiers(info, accessorDef.Attributes);

            AddTypeParts(info, sig.ReturnType);
            info.SignatureParts.Add(DisplayPart.Space());

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

        private void AddDeclaringType(MemberDefinitionInfo info, string declaringTypeName, TooltipGenericContext context)
        {
            info.SignatureParts.Add(DisplayPart.Type(declaringTypeName));
            if (context.TypeArguments.Count > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.TypeArguments.Count; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }

                    var param = context.TypeArguments[i];

                    if (_sourceUnbound)
                        info.SignatureParts.Add(DisplayPart.TypeParameterName(param));
                    else if (MetadataTooltipHelper.IsCSharpTypeKeyword(param))
                        info.SignatureParts.Add(DisplayPart.Keyword(param));
                    else
                        info.SignatureParts.Add(DisplayPart.Type(param));
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

        private static void AddTypeParts(MemberDefinitionInfo info, ImmutableArray<DisplayPart> parts)
        {
            info.SignatureParts.AddRange(parts);
        }

        private static void AddKeyword(MemberDefinitionInfo info, string keyword)
        {
            info.SignatureParts.Add(DisplayPart.Keyword(keyword));
            info.SignatureParts.Add(DisplayPart.Space());
        }

        private static void AddMethodTypeParameters(MemberDefinitionInfo info, TooltipGenericContext context)
        {
            if (context.MethodTypeParameters.Count > 0)
            {
                info.SignatureParts.Add(DisplayPart.Punctuation("<"));
                for (int i = 0; i < context.MethodTypeParameters.Count; i++)
                {
                    if (i > 0)
                    {
                        info.SignatureParts.Add(DisplayPart.Punctuation(","));
                        info.SignatureParts.Add(DisplayPart.Space());
                    }
                    info.SignatureParts.Add(DisplayPart.TypeParameterName(context.MethodTypeParameters[i]));
                }
                info.SignatureParts.Add(DisplayPart.Punctuation(">"));
            }
        }

        private static void AddMethodParameters(
            MemberDefinitionInfo info,
            MetadataReader reader,
            MethodDefinition methodDef,
            MethodSignature<ImmutableArray<DisplayPart>> sig)
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
                    var paramParts = sig.ParameterTypes[paramIndex];

                    // Check if the provider emitted a by-ref type (starts with "ref" keyword + space)
                    if (paramParts.Length >= 2
                        && paramParts[0].Kind == Microsoft.CodeAnalysis.SymbolDisplayPartKind.Keyword
                        && paramParts[0].Text == "ref")
                    {
                        bool isIn = (param.Attributes & ParameterAttributes.In) != 0;
                        bool isOut = (param.Attributes & ParameterAttributes.Out) != 0;

                        if (isOut)
                        {
                            info.SignatureParts.Add(DisplayPart.Keyword("out"));
                        }
                        else if (isIn)
                        {
                            bool isRefReadonly = HasAttribute(reader, param.GetCustomAttributes(),
                                "System.Runtime.CompilerServices", "RequiresLocationAttribute");

                            if (isRefReadonly)
                                info.SignatureParts.Add(DisplayPart.Keyword("ref readonly"));
                            else
                                info.SignatureParts.Add(DisplayPart.Keyword("in"));
                        }
                        else
                        {
                            info.SignatureParts.Add(DisplayPart.Keyword("ref"));
                        }

                        info.SignatureParts.Add(DisplayPart.Space());

                        // Add the element type parts (skip the "ref" keyword and space)
                        for (int pi = 2; pi < paramParts.Length; pi++)
                            info.SignatureParts.Add(paramParts[pi]);
                    }
                    else
                    {
                        if (HasAttribute(reader, param.GetCustomAttributes(),
                            "System.Runtime.CompilerServices", "ParamArrayAttribute")
                            || HasAttribute(reader, param.GetCustomAttributes(),
                            "System", "ParamArrayAttribute"))
                        {
                            info.SignatureParts.Add(DisplayPart.Keyword("params"));
                            info.SignatureParts.Add(DisplayPart.Space());
                        }

                        info.SignatureParts.AddRange(paramParts);
                    }

                    info.SignatureParts.Add(DisplayPart.Space());
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
