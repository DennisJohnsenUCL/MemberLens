namespace MemberLens
{
    internal readonly struct MemberInfo
    {
        public string Name { get; }
        public object TooltipSource { get; }

        public MemberInfo(string name, object tooltipSource)
        {
            Name = name;
            TooltipSource = tooltipSource;
        }
    }
}
