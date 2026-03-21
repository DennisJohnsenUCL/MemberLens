namespace MemberLens.MetadataMembers
{
    internal readonly struct MetadataMemberInfo
    {
        public string Name { get; }
        public MemberDefinitionInfo MemberDefinitionInfo { get; }

        public MetadataMemberInfo(string name, MemberDefinitionInfo memberDefinitionInfo)
        {
            Name = name;
            MemberDefinitionInfo = memberDefinitionInfo;
        }
    }
}
