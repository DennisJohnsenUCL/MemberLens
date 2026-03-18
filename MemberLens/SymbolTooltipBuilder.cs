using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens
{
    public static class SymbolTooltipBuilder
    {
        private static readonly SymbolDisplayFormat SignatureFormat =
            new SymbolDisplayFormat(
                genericsOptions:
                    SymbolDisplayGenericsOptions.IncludeTypeParameters,
                memberOptions:
                    SymbolDisplayMemberOptions.IncludeParameters |
                    SymbolDisplayMemberOptions.IncludeContainingType |
                    SymbolDisplayMemberOptions.IncludeType,
                parameterOptions:
                    SymbolDisplayParameterOptions.IncludeType |
                    SymbolDisplayParameterOptions.IncludeName |
                    SymbolDisplayParameterOptions.IncludeDefaultValue,
                miscellaneousOptions:
                    SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            );

        public static ContainerElement Build(ISymbol symbol, CancellationToken cancellationToken = default)
        {
            var elements = new List<object>();

            var signatureParts = symbol.ToDisplayParts(SignatureFormat);
            var runs = new List<ClassifiedTextRun>();

            if (symbol.Kind == SymbolKind.Field)
            {
                runs.Add(new ClassifiedTextRun(
                    PredefinedClassificationTypeNames.Text,
                    "(field) "));
            }

            runs.AddRange(signatureParts.Select(part => new ClassifiedTextRun(
                ConvertClassification(part.Kind),
                part.ToString())));

            elements.Add(new ClassifiedTextElement(runs));

            var xml = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
            var summaryText = ExtractSummary(xml);

            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.Text,
                        summaryText)));
            }

            return new ContainerElement(
                ContainerElementStyle.Stacked,
                elements);
        }

        //TODO: Do something else with this
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

        private static string ExtractSummary(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return null;

            try
            {
                var doc = XDocument.Parse(xml);
                return doc.Root?
                    .Element("summary")?
                    .Value?
                    .Trim();
            }
            catch
            {
                return null;
            }
        }
    }
}
