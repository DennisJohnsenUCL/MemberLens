using Microsoft.CodeAnalysis;

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
}
