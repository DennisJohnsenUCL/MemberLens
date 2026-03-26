using System.Collections.Generic;

namespace MemberLens.MetadataTooltips
{
    internal class MemberDefinitionInfo
    {
        public MemberKind Kind { get; set; }
        public List<DisplayPart> SignatureParts { get; set; } = new List<DisplayPart>();
        public string SummaryText { get; set; }
    }
}
