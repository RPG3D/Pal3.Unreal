using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ManagedModules;

// Build-time project checks, without a runtime assembly-set contract.
public static class ManagedAssemblyMetadata
{
    public static string Identity(byte[] image)
    {
        using var pe = Open(image);
        var reader = pe.GetMetadataReader();
        var definition = reader.GetAssemblyDefinition();
        return Name(reader, definition.Name, definition.Version, definition.Culture, definition.PublicKey, true).FullName!;
    }

    public static void ValidateEntry(byte[] image, string entryType)
    {
        using var pe = Open(image);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            if (FullTypeName(reader, handle) != entryType) continue;
            var type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0 || type.GetGenericParameters().Count != 0)
                throw new InvalidDataException("The entry type must be concrete and non-generic: " + entryType);
            if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                throw new InvalidDataException("The entry type must be public: " + entryType);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor" || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
                var signature = reader.GetBlobReader(method.Signature);
                var header = signature.ReadSignatureHeader();
                if (header.IsGeneric) signature.ReadCompressedInteger();
                if (signature.ReadCompressedInteger() == 0) return;
            }
            throw new InvalidDataException("The entry type needs a public parameterless constructor: " + entryType);
        }
        throw new InvalidDataException("Entry type is absent: " + entryType);
    }

    internal static string FullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string prefix = reader.GetString(type.Namespace);
        return type.GetDeclaringType().IsNil ? (prefix.Length == 0 ? "" : prefix + ".") + reader.GetString(type.Name)
            : FullTypeName(reader, type.GetDeclaringType()) + "+" + reader.GetString(type.Name);
    }

    private static AssemblyName Name(MetadataReader reader, StringHandle name, Version version, StringHandle culture, BlobHandle key, bool fullKey)
    {
        var identity = new AssemblyName { Name = reader.GetString(name), Version = version, CultureName = reader.GetString(culture) };
        byte[] bytes = reader.GetBlobBytes(key);
        if (fullKey && bytes.Length > 0) identity.SetPublicKey(bytes);
        else identity.SetPublicKeyToken(bytes);
        return identity;
    }

    private static PEReader Open(byte[] image) => new(new MemoryStream(image, writable: false));
}
