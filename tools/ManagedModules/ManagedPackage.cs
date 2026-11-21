using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;

namespace ManagedModules;

// Project-side delivery checks. No runtime loading protocol is added to the plugin.
public static class ManagedPackage
{
    public static readonly string[] GameAssemblies = ["Pal3.Core", "Pal3.Engine", "Pal3.Game", "Pal3.Frontend", "Pal3.UnityEngine", "Pal3A.Core", "Pal3A.Engine", "Pal3A.Game", "Pal3A.Frontend", "Pal3A.UnityEngine"];

    public static void Verify(string directory, bool editor)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "UserCode.LoadOrder.json")));
        string[] order = manifest.RootElement.GetProperty("LoadOrder").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (order.Length != order.Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw new InvalidDataException("Duplicate startup assembly names.");
        if (manifest.RootElement.GetProperty("Collectible").GetBoolean() != editor) throw new InvalidDataException("Unexpected startup assembly lifetime.");
        foreach (string name in GameAssemblies.Concat(["GameHost", "GameHost.Contracts", "ManagedSharpDemo"]))
            if (!order.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Missing ordinary startup assembly: " + name);
        var images = new List<byte[]>();
        foreach (string name in order)
        {
            if (name != Path.GetFileName(name) || name.Contains(':') || name.Contains('\\')) throw new InvalidDataException("Startup names must be simple names.");
            byte[] image = File.ReadAllBytes(Path.Combine(directory, name + ".dll"));
            if (new AssemblyName(ManagedAssemblyMetadata.Identity(image)).Name != name) throw new InvalidDataException("Filename/assembly identity mismatch: " + name);
            images.Add(image);
        }
        // Verify project-to-project startup order, using the same ordinary metadata
        // the upstream emitter uses. Framework/native SDK delivery stays with SDK.
        var positions = order.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        using var entries = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "Script/GameHost/Resources/Games.json")));
        foreach (var game in entries.RootElement.GetProperty("games").EnumerateArray())
        {
            string assembly = game.GetProperty("entryAssembly").GetString()!;
            ManagedAssemblyMetadata.ValidateEntry(File.ReadAllBytes(Path.Combine(directory, assembly + ".dll")), game.GetProperty("entryType").GetString()!);
        }
        // Independent nodes need not have the same relative order.
        // Compare only actual dependency edges.
        foreach (byte[] image in images)
        {
            using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(image));
            var reader = pe.GetMetadataReader();
            string name = reader.GetString(reader.GetAssemblyDefinition().Name);
            foreach (var handle in reader.AssemblyReferences)
            {
                string dependency = reader.GetString(reader.GetAssemblyReference(handle).Name);
                if (positions.TryGetValue(dependency, out int position) && position >= positions[name]) throw new InvalidDataException("Startup dependency is out of order: " + dependency + " -> " + name);
            }
        }
    }

    private static string FindRoot()
    {
        string path = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(path, "SharpDemo.uproject"))) path = Directory.GetParent(path)?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
        return path;
    }
}
