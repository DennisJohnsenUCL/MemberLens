using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MemberLens.Attributes;
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

            _crawler = new MetadataCrawler(_compilation);
        }

        public string GetRootKey(INamedTypeSymbol symbol) => BuildFullName(symbol) + _accessorType.ToString();

        public IEnumerable<SourceMemberInfo> GetMetadataMemberInfos(INamedTypeSymbol symbol, bool root)
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

            var ctx = new TypeDefinitionContext(mdReader.GetTypeDefinition(match.Value), peReader, mdReader);

            IEnumerable<SourceMemberInfo> items;
            if (_accessorType == AccessorType.Field)
                items = GetFieldInfos(ctx, root);

            else if (_accessorType == AccessorType.Method)
                items = GetMethodInfos(ctx, root);

            else return null;

            return items;
        }

        private IEnumerable<SourceMemberInfo> GetFieldInfos(TypeDefinitionContext ctx, bool root)
        {
            var memberInfos = ctx.TypeDefinition.GetFields()
                .Where(x => !IsBackingField(ctx.MetadataReader, x) && (root || IsAccessibleFromDerived(ctx.MetadataReader, x)))
                .Select(fieldHandle =>
                {
                    var fieldDef = ctx.MetadataReader.GetFieldDefinition(fieldHandle);
                    var fieldName = ctx.MetadataReader.GetString(fieldDef.Name);
                    return new SourceMemberInfo(fieldName, MemberDefinitionInfoFactory.FromField(ctx.MetadataReader, fieldHandle));
                })
                .ToList();

            var baseCtx = _crawler.GetBaseType(ctx);
            if (baseCtx == null) return memberInfos;

            return memberInfos.Concat(GetFieldInfos(baseCtx, root: false));
        }

        private IEnumerable<SourceMemberInfo> GetMethodInfos(TypeDefinitionContext ctx, bool root)
        {
            var memberInfos = ctx.TypeDefinition.GetMethods()
                .Where(x => !IsCtorOrExplicit(ctx.MetadataReader, x) && (root || IsAccessibleFromDerived(ctx.MetadataReader, x)))
                .Select(methodHandle =>
                {
                    var methodDef = ctx.MetadataReader.GetMethodDefinition(methodHandle);
                    var methodName = ctx.MetadataReader.GetString(methodDef.Name);
                    return new SourceMemberInfo(methodName, MemberDefinitionInfoFactory.FromMethod(ctx.MetadataReader, methodHandle));
                })
                .ToList();

            var baseCtx = _crawler.GetBaseType(ctx);
            if (baseCtx == null) return memberInfos;

            return memberInfos.Concat(GetMethodInfos(baseCtx, root: false));
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
    }
}
