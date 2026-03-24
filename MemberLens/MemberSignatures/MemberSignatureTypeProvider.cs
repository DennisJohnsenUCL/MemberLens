using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;

namespace MemberLens.MemberSignatures
{
    internal class MemberSignatureTypeProvider : ISignatureTypeProvider<string, MemberSignatureGenericContext>
    {
        public IEnumerable<string> LastTypeArguments { get; private set; }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return string.Empty;
        }

        public string GetByReferenceType(string elementType)
        {
            return string.Empty;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return string.Empty;
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            LastTypeArguments = typeArguments;

            var backtickIndex = genericType.IndexOf('`');
            if (backtickIndex >= 0)
                genericType = genericType.Substring(0, backtickIndex);
            var args = string.Join(", ", typeArguments);
            return $"{genericType}<{args}>";
        }

        public string GetGenericMethodParameter(MemberSignatureGenericContext genericContext, int index)
        {
            var methodDef = genericContext.Reader.GetMethodDefinition(genericContext.MethodHandle);
            var genericParams = methodDef.GetGenericParameters();
            var param = genericContext.Reader.GetGenericParameter(genericParams[index]);
            return genericContext.Reader.GetString(param.Name);
        }

        public string GetGenericTypeParameter(MemberSignatureGenericContext genericContext, int index)
        {
            if (genericContext.TypeArguments != null && index < genericContext.TypeArguments.Count())
                return genericContext.TypeArguments.ToArray()[index];

            var genericParams = genericContext.TypeDefinition.GetGenericParameters();
            var param = genericContext.Reader.GetGenericParameter(genericParams[index]);
            return genericContext.Reader.GetString(param.Name);
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return string.Empty;
        }

        public string GetPinnedType(string elementType)
        {
            return string.Empty;
        }

        public string GetPointerType(string elementType)
        {
            return string.Empty;
        }

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
                default: return string.Empty;
            }
        }

        public string GetSZArrayType(string elementType)
        {
            return string.Empty;
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var name = reader.GetString(typeDef.Name);
            var ns = reader.GetString(typeDef.Namespace);
            if (string.IsNullOrEmpty(ns))
                return $"global::{name}";
            return $"global::{ns}.{name}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var ns = reader.GetString(typeRef.Namespace);
            if (string.IsNullOrEmpty(ns))
                return $"global::{name}";
            return $"global::{ns}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, MemberSignatureGenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return string.Empty;
        }
    }
}
