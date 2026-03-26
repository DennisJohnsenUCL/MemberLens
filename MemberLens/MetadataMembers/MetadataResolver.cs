using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using MemberLens.Attributes;
using MemberLens.MemberSignatures;
using MemberLens.MetadataTooltips;
using MemberLens.SourceMembers;
using Microsoft.CodeAnalysis;

namespace MemberLens.MetadataMembers
{
    internal class MetadataResolver : IDisposable
    {
        private readonly MetadataCrawler _crawler;
        private readonly MemberDefinitionInfoFactory _infoFactory;
        private readonly AccessorType _accessorType;

        public MetadataResolver(MetadataCrawler crawler, MemberDefinitionInfoFactory infoFactory, AccessorType accessorType)
        {
            _crawler = crawler;
            _infoFactory = infoFactory;
            _accessorType = accessorType;
        }

        public string GetRootKey(INamedTypeSymbol symbol) => MetadataHelper.BuildFullName(symbol) + _accessorType.ToString();

        public IEnumerable<SourceMemberInfo> GetMetadataMemberInfos(INamedTypeSymbol symbol, MemberSignatureContext sigCtx, IEnumerable<string> typeArguments, bool root)
        {
            var defCtx = _crawler.GetTypeDefinitionFromSymbol(symbol);
            if (defCtx == null) return null;
            defCtx.TypeArguments = typeArguments;

            if (_accessorType == AccessorType.Field)
                return GetFieldInfos(defCtx, sigCtx, root);

            else if (_accessorType == AccessorType.Method)
                return GetMethodInfos(defCtx, sigCtx, root);

            else return null;
        }

        private IEnumerable<SourceMemberInfo> GetFieldInfos(TypeDefinitionContext defCtx, MemberSignatureContext sigCtx, bool root)
        {
            var memberInfos = defCtx.TypeDefinition.GetFields()
                .Where(x => ShouldIncludeField(defCtx.MetadataReader, x, root, sigCtx))
                .Select(fieldHandle =>
                {
                    var fieldDef = defCtx.MetadataReader.GetFieldDefinition(fieldHandle);
                    var fieldName = defCtx.MetadataReader.GetString(fieldDef.Name);
                    return new SourceMemberInfo(fieldName, _infoFactory.FromField(defCtx.MetadataReader, fieldHandle));
                })
                .ToArray();

            var baseCtx = _crawler.GetBaseType(defCtx);
            if (baseCtx == null) return memberInfos;

            return memberInfos.Concat(GetFieldInfos(baseCtx, sigCtx, root: false));
        }

        private IEnumerable<SourceMemberInfo> GetMethodInfos(TypeDefinitionContext defCtx, MemberSignatureContext sigCtx, bool root)
        {
            var memberInfos = defCtx.TypeDefinition.GetMethods()
                .Where(x => ShouldIncludeMethod(defCtx, x, root, sigCtx))
                .Select(methodHandle =>
                {
                    var methodDef = defCtx.MetadataReader.GetMethodDefinition(methodHandle);
                    var methodName = defCtx.MetadataReader.GetString(methodDef.Name);
                    return new SourceMemberInfo(methodName, _infoFactory.FromMethod(defCtx.MetadataReader, methodHandle, defCtx.TypeArguments));
                })
                .ToArray();

            var baseCtx = _crawler.GetBaseType(defCtx);
            if (baseCtx == null) return memberInfos;

            return memberInfos.Concat(GetMethodInfos(baseCtx, sigCtx, root: false));
        }

        private static bool ShouldIncludeField(MetadataReader mdReader, FieldDefinitionHandle fieldHandle, bool root, MemberSignatureContext sigCtx)
        {
            if (IsBackingField(mdReader, fieldHandle)) return false;
            if (!root && !IsAccessibleFromDerived(mdReader, fieldHandle)) return false;
            return sigCtx.IsEffectiveMember(mdReader, fieldHandle);
        }

        private static bool ShouldIncludeMethod(TypeDefinitionContext defCtx, MethodDefinitionHandle methodHandle, bool root, MemberSignatureContext sigCtx)
        {
            if (IsPropertyAccessor(defCtx.MetadataReader, methodHandle)) return false;
            if (IsCtorOrExplicit(defCtx.MetadataReader, methodHandle)) return false;
            if (!root && !IsAccessibleFromDerived(defCtx.MetadataReader, methodHandle)) return false;
            return sigCtx.IsEffectiveMember(defCtx, methodHandle);
        }

        private static bool IsBackingField(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetFieldDefinition(handle).Name);
            return MemberHelper.IsBackingField(name);
        }

        private static bool IsAccessibleFromDerived(MetadataReader reader, FieldDefinitionHandle handle)
        {
            var access = reader.GetFieldDefinition(handle).Attributes & FieldAttributes.FieldAccessMask;
            return access != FieldAttributes.Private && access != FieldAttributes.PrivateScope;
        }

        private static bool IsAccessibleFromDerived(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var access = reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
            return access != MethodAttributes.Private && access != MethodAttributes.PrivateScope;
        }

        private static bool IsCtorOrExplicit(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var name = reader.GetString(reader.GetMethodDefinition(handle).Name);
            return MemberHelper.IsCtorOrExplicit(name);
        }

        private static bool IsPropertyAccessor(MetadataReader mdReader, MethodDefinitionHandle methodHandle)
        {
            var methodDef = mdReader.GetMethodDefinition(methodHandle);
            if ((methodDef.Attributes & MethodAttributes.SpecialName) == 0)
                return false;

            var name = mdReader.GetString(methodDef.Name);
            return name.StartsWith("get_") || name.StartsWith("set_");
        }

        public void Dispose()
        {
            _crawler.Dispose();
        }
    }
}
