using System.Collections.Generic;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens.MetadataTooltips
{
    internal static class MetadataTooltipBuilder
    {
        public static ContainerElement Build(MemberDefinitionInfo info)
        {
            var elements = new List<object>();

            var runs = new List<ClassifiedTextRun>();

            if (info.Kind == MemberKind.Field)
            {
                runs.Add(new ClassifiedTextRun(
                    PredefinedClassificationTypeNames.Text,
                    "(field) "));
            }

            foreach (var part in info.SignatureParts)
            {
                runs.Add(new ClassifiedTextRun(
                    ClassificationHelper.ConvertClassification(part.Kind),
                    part.Text));
            }

            elements.Add(new ClassifiedTextElement(runs));

            if (!string.IsNullOrWhiteSpace(info.SummaryText))
            {
                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.Text,
                        info.SummaryText)));
            }

            return new ContainerElement(
                ContainerElementStyle.Stacked,
                elements);
        }
    }
}
