using System.Reflection.Metadata;

namespace MemberLens.MetadataMembers
{
    internal static class MetadataHelper
    {
        public static TypeDefinitionHandle? FindTypeDefinition(MetadataReader reader, string name, string fullName)
        {
            foreach (var tdh in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(tdh);
                if (reader.GetString(td.Name) != name)
                    continue;

                if (BuildFullName(reader, tdh) == fullName)
                    return tdh;
            }

            return null;
        }

        public static string BuildFullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            var name = reader.GetString(typeDef.Name);

            var declaringHandle = typeDef.GetDeclaringType();
            if (!declaringHandle.IsNil)
            {
                return BuildFullName(reader, declaringHandle) + "/" + name;
            }

            var ns = reader.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }
    }
}
