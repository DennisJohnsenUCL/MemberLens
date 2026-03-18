using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Adornments;

namespace MemberLens
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
                // Type-classified parts may contain embedded keywords
                // inside generic types, e.g. "Dictionary<string, int>".
                // Split them into properly classified runs.
                if (part.Kind == SymbolDisplayPartKind.ClassName)
                    AddClassifiedTypeRuns(runs, part.Text);
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

        /// <summary>
        /// Tokenizes a type string like "Dictionary<string, int>" into
        /// classified runs: type names get Type classification, C# keywords
        /// like "int" get Keyword, and punctuation (&lt; &gt; , [] ? *) gets
        /// Punctuation.
        /// </summary>
        private static void AddClassifiedTypeRuns(List<ClassifiedTextRun> runs, string text)
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
                    // Read an identifier token
                    int start = i;
                    while (i < text.Length && text[i] != '<' && text[i] != '>'
                        && text[i] != ',' && text[i] != ' ' && text[i] != '['
                        && text[i] != ']' && text[i] != '?' && text[i] != '*')
                    {
                        i++;
                    }

                    var token = text.Substring(start, i - start);

                    // Strip namespace prefix (e.g. "System.Collections.Generic.Dictionary" → "Dictionary")
                    // Dots are part of the token since they aren't break characters.
                    var lastDot = token.LastIndexOf('.');
                    if (lastDot >= 0)
                        token = token.Substring(lastDot + 1);

                    runs.Add(MemberDefinitionInfoFactory.IsCSharpTypeKeyword(token)
                        ? new ClassifiedTextRun(
                            PredefinedClassificationTypeNames.Keyword, token)
                        : new ClassifiedTextRun(
                            ClassificationHelper.ConvertClassification(SymbolDisplayPartKind.ClassName),
                            token));
                }
            }
        }
    }
}
