using Microsoft.CodeAnalysis;

namespace MemberLens
{
    internal class CompletionSymbolContext
    {
        public SymbolInfo MethodSymbolInfo { get; }
        public int ArgumentIndex { get; }
        public Compilation Compilation { get; }

        public CompletionSymbolContext(SymbolInfo methodSymbolInfo, int argumentIndex, Compilation compilation)
        {
            MethodSymbolInfo = methodSymbolInfo;
            ArgumentIndex = argumentIndex;
            Compilation = compilation;
        }
    }
}
