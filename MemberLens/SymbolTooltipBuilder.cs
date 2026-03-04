using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

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

            // 1️⃣ Signature
            var signatureParts = symbol.ToDisplayParts(SignatureFormat);

            var signatureRuns = signatureParts
                .Select(part => new ClassifiedTextRun(
                    ConvertClassification(part.Kind),
                    part.ToString()))
                .ToArray();

            elements.Add(new ClassifiedTextElement(signatureRuns));

            // 2️⃣ Documentation summary
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

        private static string ConvertClassification(SymbolDisplayPartKind kind)
        {
            switch (kind)
            {
                case SymbolDisplayPartKind.Keyword:
                    return PredefinedClassificationTypeNames.Keyword;

                case SymbolDisplayPartKind.Punctuation:
                    return PredefinedClassificationTypeNames.Punctuation;

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
