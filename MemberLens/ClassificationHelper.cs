using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.VisualStudio.Language.StandardClassification;

namespace MemberLens
{
    internal static class ClassificationHelper
    {
        internal static string ConvertClassification(SymbolDisplayPartKind kind)
        {
            switch (kind)
            {
                case SymbolDisplayPartKind.Keyword:
                    return PredefinedClassificationTypeNames.Keyword;

                case SymbolDisplayPartKind.ClassName:
                case SymbolDisplayPartKind.RecordClassName:
                case SymbolDisplayPartKind.StructName:
                case SymbolDisplayPartKind.RecordStructName:
                case SymbolDisplayPartKind.InterfaceName:
                case SymbolDisplayPartKind.EnumName:
                case SymbolDisplayPartKind.DelegateName:
                case SymbolDisplayPartKind.TypeParameterName:
                    return PredefinedClassificationTypeNames.Type;

                case SymbolDisplayPartKind.Punctuation:
                    return PredefinedClassificationTypeNames.Punctuation;

                case SymbolDisplayPartKind.Space:
                case SymbolDisplayPartKind.LineBreak:
                    return PredefinedClassificationTypeNames.WhiteSpace;

                case SymbolDisplayPartKind.Text:
                    return PredefinedClassificationTypeNames.Text;

                case SymbolDisplayPartKind.MethodName:
                    return ClassificationTypeNames.MethodName;

                case SymbolDisplayPartKind.ParameterName:
                    return ClassificationTypeNames.ParameterName;

                case SymbolDisplayPartKind.PropertyName:
                    return ClassificationTypeNames.PropertyName;

                default:
                    return PredefinedClassificationTypeNames.Identifier;
            }
        }
    }
}
