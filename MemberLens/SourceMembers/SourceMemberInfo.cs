namespace MemberLens.SourceMembers
{
    internal readonly struct SourceMemberInfo
    {
        public string Name { get; }
        public object TooltipSource { get; }

        public SourceMemberInfo(string name, object tooltipSource)
        {
            Name = name;
            TooltipSource = tooltipSource;
        }
    }
}
