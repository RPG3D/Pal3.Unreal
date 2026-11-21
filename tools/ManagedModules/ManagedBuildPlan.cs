using System.Runtime.InteropServices;

namespace ManagedModules;

public sealed record ManagedBuildPlan(string Platform, string Architecture, string TargetType, string Configuration,
    string TargetFramework, string RuntimeIdentifier, string Backend)
{
    public bool IsEditor => TargetType == "Editor";
    public bool IsMobile => Platform is "Android" or "IOS";
    public string Store => IsEditor ? "Editor" : Platform;
    public string DotNetConfiguration => Configuration is "Debug" or "DebugGame" ? "Debug" : "Release";
    public string SharedSubdirectory => IsMobile ? "" : Path.Combine("Binaries", "Managed", TargetFramework);

    public static ManagedBuildPlan Create(string root, string target, IReadOnlyDictionary<string, string> options)
    {
        string platform = target == "Editor" ? (OperatingSystem.IsWindows() ? "Win64" : "Mac") : target;
        if (platform is not ("Win64" or "Mac" or "Android" or "IOS")) throw new ArgumentException("Unknown platform: " + platform);
        string arch = options.GetValueOrDefault("architecture") ?? (platform is "Android" or "IOS" ? "arm64" : RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64");
        if (arch is not ("x64" or "arm64" or "IOSSimulator")) throw new ArgumentException("Unknown architecture: " + arch);
        if (platform == "Android" && arch != "arm64") throw new ArgumentException("The Android SDK currently supports arm64.");
        if (platform == "Win64" && arch != "x64") throw new ArgumentException("The Windows SDK currently supports x64.");
        if (platform == "IOS" && arch is not ("arm64" or "IOSSimulator")) throw new ArgumentException("The iOS SDK supports arm64 devices and arm64 simulators.");
        if (arch == "IOSSimulator" && platform != "IOS") throw new ArgumentException("IOSSimulator requires IOS.");
        string type = target == "Editor" ? "Editor" : options.GetValueOrDefault("target-type") ?? "Game";
        if (type is not ("Editor" or "Game" or "Client" or "Server")) throw new ArgumentException("Unknown target type: " + type);
        if (target != "Editor" && type == "Editor") throw new ArgumentException("Use the Editor target for editor publishing.");
        string config = options.GetValueOrDefault("configuration") ?? "Development";
        if (config is not ("Debug" or "DebugGame" or "Development" or "Shipping" or "Test")) throw new ArgumentException("Unknown configuration: " + config);
        string tfm = options.GetValueOrDefault("framework") ?? "net10.0";
        if (tfm != "net10.0") throw new ArgumentException("The current shared host targets net10.0.");
        string backend = type == "Editor" ? "CoreCLR" : platform is "Android" or "IOS" ? "Mono" : "CoreCLR";
        var runtimeSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in new[] { Path.Combine(root, "Config/DefaultEngine.ini"), Path.Combine(root, "Config", platform == "Win64" ? "Windows" : platform, (platform == "Win64" ? "Windows" : platform) + "Engine.ini") })
        {
            if (!File.Exists(path)) continue;
            string section = "";
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith('[')) { section = line; continue; }
                if (section != "[UnrealSharp]" || line.StartsWith(';')) continue;
                int split = line.IndexOf('=');
                if (split < 0) continue;
                string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
                if (key.Equals("DotNetRuntime", StringComparison.OrdinalIgnoreCase) || key.Equals("DotNetRuntimeEditor", StringComparison.OrdinalIgnoreCase)) runtimeSettings[key] = value;
            }
        }
        if (runtimeSettings.TryGetValue("DotNetRuntime", out string? runtime)) backend = CanonicalBackend(runtime);
        if (type == "Editor" && runtimeSettings.TryGetValue("DotNetRuntimeEditor", out string? editorRuntime)) backend = CanonicalBackend(editorRuntime);
        backend = options.GetValueOrDefault("backend") ?? backend;
        if (backend is not ("Mono" or "CoreCLR")) throw new ArgumentException("Unknown backend.");
        string rid = arch == "IOSSimulator" ? "iossimulator-arm64" : (platform switch { "Win64" => "win", "Mac" => "osx", "Android" => "android", _ => "ios" }) + "-" + arch;
        return new(platform, arch, type, config, tfm, rid, backend);
    }

    private static string CanonicalBackend(string value) => value.Equals("Mono", StringComparison.OrdinalIgnoreCase) ? "Mono"
        : value.Equals("CoreCLR", StringComparison.OrdinalIgnoreCase) ? "CoreCLR" : throw new ArgumentException("Invalid runtime configuration: " + value);

}
