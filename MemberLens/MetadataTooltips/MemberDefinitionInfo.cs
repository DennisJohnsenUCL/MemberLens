using System.Collections.Generic;

namespace MemberLens
{
    /// <summary>
    /// Holds pre-extracted metadata about a member definition (field or method)
    /// so the MetadataReader doesn't need to be passed around.
    /// </summary>
    internal class MemberDefinitionInfo
    {
        public MemberKind Kind { get; set; }

        /// <summary>
        /// Display parts for the signature, using SymbolDisplayPartKind
        /// so we can reuse the same classification logic as Roslyn symbols.
        /// </summary>
        public List<DisplayPart> SignatureParts { get; set; } = new List<DisplayPart>();

        /// <summary>
        /// The XML doc summary text, if any.
        /// </summary>
        public string SummaryText { get; set; }

        public HashSet<string> TypeParameterNames { get; set; }
    }
}
