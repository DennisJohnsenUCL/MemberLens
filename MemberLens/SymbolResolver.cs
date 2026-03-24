using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MemberLens.Attributes;
using MemberLens.MemberSignatures;
using MemberLens.MetadataMembers;
using MemberLens.SourceMembers;
using Microsoft.CodeAnalysis;

namespace MemberLens
{
    internal class SymbolResolver
    {
        private readonly AccessorType _accessorType;
        private readonly MetadataResolver _metadataResolver;

        public SymbolResolver(AccessorType accessorType, MetadataResolver metadataResolver)
        {
            _accessorType = accessorType;
            _metadataResolver = metadataResolver;
        }

        public IEnumerable<SourceMemberInfo> GetSourceMemberInfos(INamedTypeSymbol symbol, MemberSignatureContext sigCtx, bool root)
        {
            if (_accessorType == AccessorType.Field)
            {
                return GetFieldMemberInfos(symbol, sigCtx, root);
            }
            else if (_accessorType == AccessorType.Method)
            {
                return GetMethodMemberInfos(symbol, sigCtx, root);
            }
            else return null;
        }

        private IEnumerable<SourceMemberInfo> GetFieldMemberInfos(INamedTypeSymbol symbol, MemberSignatureContext sigCtx, bool root)
        {
            var fieldMemberInfos = symbol.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(x => !IsBackingField(x) && (root || IsAccessibleFromDerived(x)))
                .Select(x =>
                {
                    //TODO: Remove
                    Debug.WriteLine("Symbol   " + sigCtx.GetSymbolSignature(x));
                    return new SourceMemberInfo(x.Name, x);
                });

            var baseSymbol = symbol.BaseType;
            if (baseSymbol == null) return fieldMemberInfos;
            if (MemberHelper.IsCoreLibAssembly(baseSymbol.ContainingNamespace.ToDisplayString()))
                return fieldMemberInfos;

            if (baseSymbol.Locations[0].IsInSource)
                return fieldMemberInfos.Concat(GetFieldMemberInfos(baseSymbol, sigCtx, root: false));

            if (baseSymbol.Locations[0].IsInMetadata)
            {
                var typeArguments = baseSymbol.TypeArguments.Select(x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                return fieldMemberInfos.Concat(_metadataResolver.GetMetadataMemberInfos(baseSymbol, sigCtx, typeArguments, root: false));
            }

            return fieldMemberInfos;
        }

        private IEnumerable<SourceMemberInfo> GetMethodMemberInfos(INamedTypeSymbol symbol, MemberSignatureContext sigCtx, bool root)
        {
            var methodMemberInfos = symbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(x => !IsCtorOrExplicit(x) && (root || IsAccessibleFromDerived(x)))
                .Select(x =>
                {
                    //TODO: Remove
                    Debug.WriteLine("Symbol   " + sigCtx.GetSymbolSignature(x));
                    return new SourceMemberInfo(x.Name, x);
                });

            var baseSymbol = symbol.BaseType;
            if (baseSymbol == null) return methodMemberInfos;
            if (MemberHelper.IsCoreLibAssembly(baseSymbol.ContainingNamespace.ToDisplayString()))
                return methodMemberInfos;

            if (baseSymbol.Locations[0].IsInSource)
                return methodMemberInfos.Concat(GetMethodMemberInfos(baseSymbol, sigCtx, root: false));

            if (baseSymbol.Locations[0].IsInMetadata)
            {
                var typeArguments = baseSymbol.TypeArguments.Select(x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                return methodMemberInfos.Concat(_metadataResolver.GetMetadataMemberInfos(baseSymbol, sigCtx, typeArguments, root: false));
            }

            return methodMemberInfos;
        }

        private bool IsAccessibleFromDerived(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility != Accessibility.Private;
        }

        private bool IsBackingField(IFieldSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsBackingField(name);
        }

        private bool IsCtorOrExplicit(IMethodSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsCtorOrExplicit(name);
        }
    }
}
