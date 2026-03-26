namespace MemberLens.SourceMembers
{
    internal static class MemberHelper
    {
        public static bool IsCoreLibAssembly(string assemblyName)
        {
            return assemblyName.StartsWith("System") || assemblyName.StartsWith("Microsoft");
        }

        public static bool IsBackingField(string name)
        {
            return name.StartsWith("<") && name.EndsWith(">k__BackingField");
        }

        public static bool IsCtorOrExplicit(string name)
        {
            return name == ".ctor" || name.Contains(".");
        }
    }
}
