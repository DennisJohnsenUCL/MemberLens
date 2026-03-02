using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FixtureBuilder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;

namespace MemberLens
{
    internal class MemberAccessorCompletionSource : IAsyncCompletionSource
    {
        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            Debug.WriteLine("1");
            var snapshot = triggerLocation.Snapshot;
            var doc = snapshot.TextBuffer.GetRelatedDocuments().FirstOrDefault();
            if (doc == null) return CompletionContext.Empty;

            var syntaxTree = await doc.GetSyntaxTreeAsync(token);
            if (syntaxTree == null) return CompletionContext.Empty;

            var root = await syntaxTree.GetRootAsync(token);

            if (token.IsCancellationRequested) return CompletionContext.Empty;
            Debug.WriteLine("2");
            var position = triggerLocation.Position;
            var locationToken = root.FindToken(position);
            var tokenParent = locationToken.Parent;
            if (tokenParent == null) return CompletionContext.Empty;
            Debug.WriteLine("2.1");
            var argumentListSyntax = tokenParent.FirstAncestorOrSelf<ArgumentListSyntax>();
            if (argumentListSyntax == null) return CompletionContext.Empty;
            Debug.WriteLine("2.2");
            var expressionSyntax = argumentListSyntax.Parent;
            if (expressionSyntax == null) return CompletionContext.Empty;
            Debug.WriteLine("2.3");
            int argumentSymbolIndex = argumentListSyntax.Arguments.GetSeparators().Count(separator => separator.SpanStart < position);
            Debug.WriteLine("3");
            var semanticModel = await doc.GetSemanticModelAsync(token);
            if (semanticModel == null) return CompletionContext.Empty;
            Debug.WriteLine("3.1");
            if (token.IsCancellationRequested) return CompletionContext.Empty;
            Debug.WriteLine("3.2");
            var methodSymbolInfo = semanticModel.GetSymbolInfo(expressionSyntax, token);
            var methodSymbol = methodSymbolInfo.Symbol as IMethodSymbol;
            if (methodSymbol == null)
            {
                var candidateSymbols = methodSymbolInfo.CandidateSymbols;
                if (candidateSymbols.Length == 0) return CompletionContext.Empty;
                //TODO: Iterate through and try to find one with MemberAccessor
                else
                {
                    methodSymbol = candidateSymbols[0] as IMethodSymbol;
                    if (methodSymbol == null) return CompletionContext.Empty;
                }
            }
            Debug.WriteLine("3.3");
            var methodParameterSymbols = methodSymbol.Parameters;
            Debug.WriteLine("4");

            Debug.WriteLine("argumentSymbolIndex " + argumentSymbolIndex);
            Debug.WriteLine("methodParameterSymbols.Length " + methodParameterSymbols.Length);
            if (argumentSymbolIndex >= methodParameterSymbols.Length) return CompletionContext.Empty;
            Debug.WriteLine("4.1");
            var parameterSymbol = methodParameterSymbols[argumentSymbolIndex];
            if (parameterSymbol.Type.Name != "String") return CompletionContext.Empty;

            var attributes = parameterSymbol.GetAttributes();
            Debug.WriteLine(parameterSymbol.Name);
            Debug.WriteLine(attributes.Length);
            foreach (var item in attributes)
            {
                Debug.WriteLine("Attribute class name " + item.AttributeClass?.Name);
            }
            Debug.WriteLine(nameof(MemberAccessorAttribute));
            var memberAccessorAttribute = attributes.FirstOrDefault(ad => ad.AttributeClass?.Name == nameof(MemberAccessorAttribute));

            if (memberAccessorAttribute == null) return CompletionContext.Empty;
            Debug.WriteLine("5");
            var constructorArgs = memberAccessorAttribute.ConstructorArguments;
            if (constructorArgs.Length < 2) return CompletionContext.Empty;
            Debug.WriteLine("5.1");
            var accessorTypes = (AccessorTypes)(int)constructorArgs[0].Value;

            INamedTypeSymbol sourceType = null;

            if (constructorArgs[1].Kind == TypedConstantKind.Type)
            {
                sourceType = (INamedTypeSymbol)constructorArgs[1].Value;
            }
            else if (constructorArgs.Length >= 3)
            {
                var genericSources = (GenericSources)(int)constructorArgs[1].Value;
                var genericIndex = (int)constructorArgs[2].Value;

                if (genericSources == GenericSources.Method)
                {
                    if (genericIndex >= methodSymbol.TypeArguments.Length) return CompletionContext.Empty;
                    Debug.WriteLine("5.2");
                    sourceType = (INamedTypeSymbol)methodSymbol.TypeArguments[genericIndex];
                }
                else if (genericSources == GenericSources.Class)
                {
                    var sourceClass = methodSymbol.ContainingType;
                    if (genericIndex >= sourceClass.TypeArguments.Length) return CompletionContext.Empty;
                    Debug.WriteLine("5.3");
                    sourceType = (INamedTypeSymbol)sourceClass.TypeArguments[genericIndex];
                }
                else return CompletionContext.Empty;
                Debug.WriteLine("5.4");
            }
            else return CompletionContext.Empty;
            Debug.WriteLine("5.5");

            if (sourceType == null) return CompletionContext.Empty;

            Debug.WriteLine("7");
            ImmutableArray<ISymbol> sourceMembers;

            if (accessorTypes == AccessorTypes.Field)
            {
                sourceMembers = sourceType.GetMembers().OfType<IFieldSymbol>().Select(x => (ISymbol)x).ToImmutableArray();
            }
            else if (accessorTypes == AccessorTypes.Method)
            {
                sourceMembers = sourceType.GetMembers().OfType<IMethodSymbol>().Select(x => (ISymbol)x).ToImmutableArray();
            }
            else return CompletionContext.Empty;

            //TODO: Handle out-of-solution types

            //TODO: Filter away BCL and others
            //TODO: Filter by current SnapshotSpan

            //TODO: Filter .ctor methods

            Debug.WriteLine("8");
            //TODO: Better CompletionItem overloads?
            var completionItems = sourceMembers.Select(x => new CompletionItem($"\"{x.Name}\"", this)).ToImmutableArray();
            //TODO: Better CompletionContext overloads?
            var completionContext = new CompletionContext(completionItems);
            return completionContext;
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            //TODO: Implement
            return Task.FromResult<object>(string.Empty);
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            var snapshot = triggerLocation.Snapshot;
            var doc = snapshot.TextBuffer.GetRelatedDocuments().FirstOrDefault();
            if (doc == null) return CompletionStartData.DoesNotParticipateInCompletion;

            var position = triggerLocation.Position;

            //TODO: Handle invalid initial position, or start of argument before typing
            //TODO: Handle "
            //TODO: Exit early if not in method invocation (check for ( to left)
            //TODO: Handle trivia (space)
            var initial = snapshot[position];

            if (position != 0 && (snapshot[position - 1] == '(' || snapshot[position - 1] == ',') || position > 1 && snapshot[position - 1] == ' ' && snapshot[position - 2] == ',')
                return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, position, 0));

            if (!char.IsLetterOrDigit(initial) && initial != '_')
                return CompletionStartData.DoesNotParticipateInCompletion;

            var start = position;
            while (true)
            {
                if (start == 0) break;
                var c = snapshot[start - 1];
                if (!char.IsLetterOrDigit(c) && c != '_') break;
                else
                {
                    start--;
                }
            }

            var end = position + 1;
            while (true)
            {
                if (end == snapshot.Length - 1) break;
                var c = snapshot[end];
                if (!char.IsLetterOrDigit(c) && c != '_') break;
                else
                {
                    end++;
                }
            }

            var snapshotSpan = new SnapshotSpan(snapshot, start, end - start);

            return new CompletionStartData(CompletionParticipation.ProvidesItems, snapshotSpan);
        }
    }
}
