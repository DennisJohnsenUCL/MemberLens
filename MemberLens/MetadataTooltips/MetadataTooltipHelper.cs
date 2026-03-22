namespace MemberLens.MetadataTooltips
{
    internal static class MetadataTooltipHelper
    {
        public static bool IsCSharpTypeKeyword(string typeName)
        {
            switch (typeName)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "char":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "object":
                case "void":
                case "nint":
                case "nuint":
                    return true;
                default:
                    return false;
            }
        }
    }
}
