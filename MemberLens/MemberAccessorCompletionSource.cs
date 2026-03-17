using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using MemberLens.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens
{
    internal class CompletionSymbolContext
    {
        public SymbolInfo MethodSymbolInfo { get; }
        public int ArgumentIndex { get; }
        public SemanticModel SemanticModel { get; }

        public CompletionSymbolContext(SymbolInfo methodSymbolInfo, int argumentIndex, SemanticModel semanticModel)
        {
            MethodSymbolInfo = methodSymbolInfo;
            ArgumentIndex = argumentIndex;
            SemanticModel = semanticModel;
        }
    }

    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        //TODO: Empty() method.

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            var symCtx = await GetSymbolContextAsync(triggerLocation, token);
            if (symCtx == null) return CompletionContext.Empty;

            var symbolAndAttribute = GetSymbolAndAttribute(symCtx.MethodSymbolInfo, symCtx.ArgumentIndex);
            if (symbolAndAttribute == null) return CompletionContext.Empty;
            var (methodSymbol, memberAccessorAttribute) = symbolAndAttribute.Value;

            var constructorArgs = memberAccessorAttribute.ConstructorArguments;
            if (constructorArgs.Length < 2) return CompletionContext.Empty;

            var sourceType = GetSourceType(methodSymbol, constructorArgs);
            if (sourceType == null) return CompletionContext.Empty;

            var accessorTypes = (AccessorType)(int)constructorArgs[0].Value;

            ImmutableArray<CompletionItem>? completionItems;
            if (sourceType.Locations[0].IsInSource)
            {
                completionItems = GetSourceCompletionItems(sourceType, accessorTypes, this);
            }
            else if (sourceType.Locations[0].IsInMetadata)
            {
                completionItems = GetMetadataCompletionItems(sourceType, accessorTypes, this, symCtx.SemanticModel);
            }
            else return CompletionContext.Empty;

            if (completionItems == null) return CompletionContext.Empty;

            var completionContext = new CompletionContext(completionItems.Value);
            return completionContext;
        }

        public async Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (!(item.Properties.GetProperty("symbol") is ISymbol symbol))
                return Task.FromResult<object>(string.Empty);

            return SymbolTooltipBuilder.Build(symbol, token);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            if (!snapshot.TextBuffer.GetRelatedDocuments().Any())
                return CompletionStartData.DoesNotParticipateInCompletion;

            var position = triggerLocation.Position;

            if (position == snapshot.Length || position - 1 < 0)
                return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));

            var initial = snapshot[position - 1];
            if (char.IsLetterOrDigit(initial) || initial == '_' || initial == '"')
            {
                var snapshotSpan = GetSnapshotSpan(snapshot, position);
                return new CompletionStartData(CompletionParticipation.ProvidesItems, snapshotSpan);
            }

            return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));
        }

        private static async Task<CompletionSymbolContext> GetSymbolContextAsync(SnapshotPoint triggerLocation, CancellationToken token = default)
        {
            var doc = triggerLocation.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (doc == null) return null;

            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            if (syntaxTree == null) return null;

            var root = await syntaxTree.GetRootAsync(token);

            if (token.IsCancellationRequested) return null;

            var position = triggerLocation.Position;
            var tokenParent = root.FindToken(position).Parent;
            if (tokenParent == null) return null;

            var argumentListSyntax = tokenParent.FirstAncestorOrSelf<ArgumentListSyntax>();
            if (argumentListSyntax == null) return null;

            var expressionSyntax = argumentListSyntax.Parent;
            if (expressionSyntax == null) return null;

            var semanticModel = await doc.GetSemanticModelAsync(token);
            if (semanticModel == null) return null;

            if (token.IsCancellationRequested) return null;

            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            int argumentIndex = argumentListSyntax.Arguments.GetSeparators().Count(separator => separator.SpanStart < position);

            return new CompletionSymbolContext(methodSymbolInfo, argumentIndex, semanticModel);
        }

        private static (IMethodSymbol MethodSymbol, AttributeData MemberAccessorAttribute)? GetSymbolAndAttribute(
            SymbolInfo methodSymbolInfo, int argumentSymbolIndex)
        {
            var candidates = methodSymbolInfo.Symbol is IMethodSymbol directMatch
                ? new[] { directMatch }
                : methodSymbolInfo.CandidateSymbols.Cast<IMethodSymbol>();

            foreach (var candidate in candidates)
            {
                if (argumentSymbolIndex >= candidate.Parameters.Length) continue;

                var parameterSymbol = candidate.Parameters[argumentSymbolIndex];
                if (parameterSymbol.Type.Name != "String") continue;

                var memberAccessorAttribute = parameterSymbol.GetAttributes()
                    .FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));
                if (memberAccessorAttribute == null) continue;

                return (candidate, memberAccessorAttribute);
            }

            return null;
        }

        private static INamedTypeSymbol GetSourceType(IMethodSymbol methodSymbol, ImmutableArray<TypedConstant> constructorArgs)
        {
            INamedTypeSymbol sourceType;
            if (constructorArgs[1].Kind == TypedConstantKind.Type)
            {
                sourceType = (INamedTypeSymbol)constructorArgs[1].Value;
            }
            else if (constructorArgs.Length == 3)
            {
                var genericSources = (GenericSource)(int)constructorArgs[1].Value;
                var genericIndex = (int)constructorArgs[2].Value;

                if (genericSources == GenericSource.Method)
                {
                    if (genericIndex >= methodSymbol.TypeArguments.Length) return null;
                    sourceType = (INamedTypeSymbol)methodSymbol.TypeArguments[genericIndex];
                }
                else if (genericSources == GenericSource.Class)
                {
                    var sourceClass = methodSymbol.ContainingType;
                    if (genericIndex >= sourceClass.TypeArguments.Length) return null;
                    sourceType = (INamedTypeSymbol)sourceClass.TypeArguments[genericIndex];
                }
                else return null;
            }
            else return null;

            return sourceType;
        }

        private static ImmutableArray<CompletionItem>? GetSourceCompletionItems(INamedTypeSymbol sourceType, AccessorType accessorTypes, MemberAccessorCompletionSource source)
        {
            IEnumerable<ISymbol> sourceMembers;

            if (accessorTypes == AccessorType.Field)
            {
                sourceMembers = sourceType.GetMembers().OfType<IFieldSymbol>().Select(x => (ISymbol)x);
            }
            else if (accessorTypes == AccessorType.Method)
            {
                sourceMembers = sourceType.GetMembers().OfType<IMethodSymbol>().Select(x => (ISymbol)x);
            }
            else return null;

            var icon = GetIcon(accessorTypes);

            var completionItems = sourceMembers
                .Where(symbol => symbol.Name != ".ctor")
                .Select(symbol =>
                {
                    var fullName = $"\"{symbol.Name}\"";

                    var item = new CompletionItem(
                        displayText: symbol.Name,
                        source: source,
                        icon: icon,
                        filters: ImmutableArray<CompletionFilter>.Empty,
                        suffix: string.Empty,
                        insertText: fullName,
                        sortText: fullName,
                        filterText: fullName,
                        attributeIcons: ImmutableArray<ImageElement>.Empty);

                    item.Properties.AddProperty("symbol", symbol);

                    return item;
                })
                .ToImmutableArray();

            return completionItems;
        }

        private static ImageElement GetIcon(AccessorType accessorTypes)
        {
            ImageElement icon;
            switch (accessorTypes)
            {
                case AccessorType.Field:
                    icon = new ImageElement(KnownMonikers.Field.ToImageId());
                    break;
                case AccessorType.Method:
                    icon = new ImageElement(KnownMonikers.Method.ToImageId());
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return icon;
        }

        private static SnapshotSpan GetSnapshotSpan(ITextSnapshot snapshot, int position)
        {
            var start = position - 1;
            while (true)
            {
                if (start <= 0) break;
                var c = snapshot[start - 1];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '"') break;
                else
                {
                    start--;
                }
            }

            var end = position;
            while (true)
            {
                if (end >= snapshot.Length) break;
                var c = snapshot[end];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '"') break;
                else
                {
                    end++;
                }
            }

            return new SnapshotSpan(snapshot, start, end - start);
        }

        private static ImmutableArray<CompletionItem>? GetMetadataCompletionItems(
            INamedTypeSymbol sourceType,
            AccessorType accessorType,
            MemberAccessorCompletionSource source,
            SemanticModel semanticModel)
        {
            //TODO: Rebuild Nuget project, add generic source class, nested source class, test match

            var compilation = semanticModel.Compilation;

            //TODO: Filter out sourceType.ContainingAssembly.Name.StartsWith("System., Microsoft.");

            if (!(compilation.GetMetadataReference(
                sourceType.ContainingAssembly) is PortableExecutableReference reference)) return null;

            var path = reference.FilePath;
            if (path == null) return null;

            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream, PEStreamOptions.PrefetchMetadata))
            {
                var mdReader = peReader.GetMetadataReader();

                var sourceFullName = BuildFullName(sourceType);

                var match = mdReader.TypeDefinitions
                    .Where(h => mdReader.GetString(mdReader.GetTypeDefinition(h).Name) == sourceType.MetadataName)
                    .FirstOrDefault(td => BuildFullName(mdReader, td) == sourceFullName);

                //TODO: This probably does nothing. Find a better way.
                if (match.IsNil) return null;

                //TODO: Filter out explicit interfaces implementations -> .Contains(".")

                if (accessorType == AccessorType.Field)
                {
                    var typeDef = mdReader.GetTypeDefinition(match);
                    var icon = new ImageElement(KnownMonikers.Field.ToImageId());

                    return typeDef.GetFields()
                        .Select(fieldHandle =>
                    {
                        var fieldDef = mdReader.GetFieldDefinition(fieldHandle);
                        var fieldName = mdReader.GetString(fieldDef.Name);
                        var fullName = $"\"{fieldName}\"";

                        var item = new CompletionItem(
                            displayText: fieldName,
                            source: source,
                            icon: icon,
                            filters: ImmutableArray<CompletionFilter>.Empty,
                            suffix: string.Empty,
                            insertText: fullName,
                            sortText: fullName,
                            filterText: fullName,
                            attributeIcons: ImmutableArray<ImageElement>.Empty);

                        //TODO: Add property

                        return item;
                    })
                        .ToImmutableArray();
                }
                else if (accessorType == AccessorType.Method)
                {
                    var typeDef = mdReader.GetTypeDefinition(match);
                    var icon = new ImageElement(KnownMonikers.Method.ToImageId());

                    return typeDef.GetMethods()
                        .Select(methodHandle =>
                        {
                            var methodDef = mdReader.GetMethodDefinition(methodHandle);
                            var methodName = mdReader.GetString(methodDef.Name);
                            var fullName = $"\"{methodName}\"";

                            var item = new CompletionItem(
                                displayText: methodName,
                                source: source,
                                icon: icon,
                                filters: ImmutableArray<CompletionFilter>.Empty,
                                suffix: string.Empty,
                                insertText: fullName,
                                sortText: fullName,
                                filterText: fullName,
                                attributeIcons: ImmutableArray<ImageElement>.Empty);

                            //TODO: Add property

                            return item;
                        })
                        .ToImmutableArray();
                }
                else throw new InvalidOperationException();
            }
        }

        private static string BuildFullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var name = reader.GetString(typeDef.Name);

            var declaringHandle = typeDef.GetDeclaringType();
            if (!declaringHandle.IsNil)
            {
                return BuildFullName(reader, declaringHandle) + "/" + name;
            }

            var ns = reader.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
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
    }
}
