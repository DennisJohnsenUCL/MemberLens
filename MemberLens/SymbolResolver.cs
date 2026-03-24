using System.Collections.Generic;
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
                .Where(x => ShouldIncludeField(x, root, sigCtx))
                .Select(x => new SourceMemberInfo(x.Name, x))
                .ToArray();

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
                .Where(x => ShouldIncludeMethod(x, root, sigCtx))
                .Select(x => new SourceMemberInfo(x.Name, x))
                .ToArray();

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

        private static bool ShouldIncludeField(IFieldSymbol symbol, bool root, MemberSignatureContext sigCtx)
        {
            if (IsBackingField(symbol)) return false;
            if (!root && !IsAccessibleFromDerived(symbol)) return false;
            return sigCtx.IsEffectiveMember(symbol);
        }

        private static bool ShouldIncludeMethod(IMethodSymbol symbol, bool root, MemberSignatureContext sigCtx)
        {
            if (IsPropertyAccessor(symbol)) return false;
            if (IsCtorOrExplicit(symbol)) return false;
            if (!root && !IsAccessibleFromDerived(symbol)) return false;
            return sigCtx.IsEffectiveMember(symbol);
        }

        private static bool IsAccessibleFromDerived(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility != Accessibility.Private;
        }

        private static bool IsBackingField(IFieldSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsBackingField(name);
        }

        private static bool IsCtorOrExplicit(IMethodSymbol symbol)
        {
            var name = symbol.Name;
            return MemberHelper.IsCtorOrExplicit(name);
        }

        private static bool IsPropertyAccessor(IMethodSymbol symbol)
        {
            return symbol.AssociatedSymbol != null && symbol.AssociatedSymbol is IPropertySymbol;
        }
    }
}
