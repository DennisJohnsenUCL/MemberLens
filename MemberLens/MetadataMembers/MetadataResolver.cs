using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MemberLens.Attributes;
using MemberLens.MemberSignatures;
using MemberLens.MetadataTooltips;
using MemberLens.SourceMembers;
using Microsoft.CodeAnalysis;

namespace MemberLens.MetadataMembers
{
    internal class MetadataResolver
    {
        private readonly Compilation _compilation;
        private readonly AccessorType _accessorType;

        private readonly MetadataCrawler _crawler;

        public MetadataResolver(Compilation compilation, AccessorType accessorType)
        {
            _compilation = compilation;
            _accessorType = accessorType;

            //TODO: Move up
            _crawler = new MetadataCrawler(_compilation);
        }

        public string GetRootKey(INamedTypeSymbol symbol) => BuildFullName(symbol) + _accessorType.ToString();

        public IEnumerable<SourceMemberInfo> GetMetadataMemberInfos(INamedTypeSymbol symbol, MemberSignatureContext sigCtx, IEnumerable<string> typeArguments, bool root)
        {
            var sourceFullName = BuildFullName(symbol);

            var assembly = symbol.ContainingAssembly;

            if (MemberHelper.IsCoreLibAssembly(assembly.Name)) return null;

            if (!(_compilation.GetMetadataReference(
                assembly) is PortableExecutableReference reference)) return null;

            var path = reference.FilePath;
            if (path == null) return null;

            var stream = File.OpenRead(path);
            var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            var mdReader = peReader.GetMetadataReader();

            var match = MetadataHelper.FindTypeDefinition(mdReader, symbol.MetadataName, sourceFullName);
            if (match == null || match.Value.IsNil || match.Value == default) return null;

            var defCtx = new TypeDefinitionContext(mdReader.GetTypeDefinition(match.Value), peReader, mdReader, typeArguments);

            IEnumerable<SourceMemberInfo> items;
            if (_accessorType == AccessorType.Field)
                items = GetFieldInfos(defCtx, sigCtx, root);

            else if (_accessorType == AccessorType.Method)
                items = GetMethodInfos(defCtx, sigCtx, root);

            else return null;

            return items;
        }

        private IEnumerable<SourceMemberInfo> GetFieldInfos(TypeDefinitionContext defCtx, MemberSignatureContext sigCtx, bool root)
        {
            var memberInfos = defCtx.TypeDefinition.GetFields()
                .Where(x => ShouldIncludeField(defCtx.MetadataReader, x, root, sigCtx))
                .Select(fieldHandle =>
                {
                    var fieldDef = defCtx.MetadataReader.GetFieldDefinition(fieldHandle);
                    var fieldName = defCtx.MetadataReader.GetString(fieldDef.Name);
                    return new SourceMemberInfo(fieldName, MemberDefinitionInfoFactory.FromField(defCtx.MetadataReader, fieldHandle));
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
                    return new SourceMemberInfo(methodName, MemberDefinitionInfoFactory.FromMethod(defCtx.MetadataReader, methodHandle));
                })
                .ToArray();

            var baseCtx = _crawler.GetBaseType(defCtx);
            if (baseCtx == null) return memberInfos;

            return memberInfos.Concat(GetMethodInfos(baseCtx, sigCtx, root: false));
        }

        private static string BuildFullName(INamedTypeSymbol symbol)
        {
            var name = symbol.MetadataName;

            if (symbol.ContainingType != null)
            {
                return BuildFullName(symbol.ContainingType) + "/" + name;
            }

            var cns = symbol.ContainingNamespace;
            var ns = cns != null && !cns.IsGlobalNamespace
                ? cns.ToDisplayString()
                : null;

            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
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
    }
}
