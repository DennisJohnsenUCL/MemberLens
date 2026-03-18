using Microsoft.CodeAnalysis;

namespace MemberLens
{
    /// <summary>
    /// A single classified chunk of display text, mirroring Roslyn's SymbolDisplayPart.
    /// </summary>
    internal readonly struct DisplayPart
    {
        public SymbolDisplayPartKind Kind { get; }
        public string Text { get; }

        public DisplayPart(SymbolDisplayPartKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        // Convenience factory methods to keep call sites readable.

        public static DisplayPart Keyword(string text) =>
            new DisplayPart(SymbolDisplayPartKind.Keyword, text);

        public static DisplayPart Type(string text) =>
            new DisplayPart(SymbolDisplayPartKind.ClassName, text);

        public static DisplayPart Punctuation(string text) =>
            new DisplayPart(SymbolDisplayPartKind.Punctuation, text);

        public static DisplayPart Space() =>
            new DisplayPart(SymbolDisplayPartKind.Space, " ");

        public static DisplayPart MethodName(string text) =>
            new DisplayPart(SymbolDisplayPartKind.MethodName, text);

        public static DisplayPart PropertyName(string text) =>
            new DisplayPart(SymbolDisplayPartKind.PropertyName, text);

        public static DisplayPart ParameterName(string text) =>
            new DisplayPart(SymbolDisplayPartKind.ParameterName, text);

        public static DisplayPart Plain(string text) =>
            new DisplayPart(SymbolDisplayPartKind.Text, text);

        public static DisplayPart FieldName(string text) =>
            new DisplayPart(SymbolDisplayPartKind.FieldName, text);

        public static DisplayPart TypeParameterName(string text) =>
            new DisplayPart(SymbolDisplayPartKind.TypeParameterName, text);
    }
}
