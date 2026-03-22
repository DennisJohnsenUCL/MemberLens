using System.Collections.Generic;
using Microsoft.CodeAnalysis;
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
                if (part.Kind == SymbolDisplayPartKind.ClassName)
                    AddClassifiedTypeRuns(runs, part.Text, info.TypeParameterNames);
                else
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

        private static void AddClassifiedTypeRuns(
            List<ClassifiedTextRun> runs, string text, HashSet<string> typeParameterNames)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (c == '<' || c == '>' || c == ',' || c == '[' || c == ']'
                    || c == '?' || c == '*')
                {
                    runs.Add(new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.Punctuation,
                        c.ToString()));
                    i++;
                }
                else if (c == ' ')
                {
                    runs.Add(new ClassifiedTextRun(
                        PredefinedClassificationTypeNames.WhiteSpace,
                        " "));
                    i++;
                }
                else
                {
                    int start = i;
                    while (i < text.Length && text[i] != '<' && text[i] != '>'
                        && text[i] != ',' && text[i] != ' ' && text[i] != '['
                        && text[i] != ']' && text[i] != '?' && text[i] != '*')
                    {
                        i++;
                    }

                    var token = text.Substring(start, i - start);

                    var lastDot = token.LastIndexOf('.');
                    if (lastDot >= 0)
                        token = token.Substring(lastDot + 1);

                    if (MetadataTooltipHelper.IsCSharpTypeKeyword(token))
                        runs.Add(new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.Keyword, token));
                    else if (typeParameterNames != null && typeParameterNames.Contains(token))
                        runs.Add(new ClassifiedTextRun(
                            ClassificationHelper.ConvertClassification(
                                SymbolDisplayPartKind.TypeParameterName), token));
                    else
                        runs.Add(new ClassifiedTextRun(
                            ClassificationHelper.ConvertClassification(
                                SymbolDisplayPartKind.ClassName), token));
                }
            }
        }
    }
}
