using System.Diagnostics;
using System.Text.Json;
using ManagedModules;

if (args.Length < 2) throw new ArgumentException("ModulePublisher <Editor|Android|IOS|Win64|Mac> all [--engine-root PATH --publish-host --verify]");
string root = Path.GetFullPath(AppContext.BaseDirectory);
while (!File.Exists(Path.Combine(root, "SharpDemo.uproject"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
if (args[1] != "all") throw new ArgumentException("Both game projects are built by the ordinary UnrealSharp solution; use all.");
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (int index = 2; index < args.Length; index++)
{
    string key = args[index].StartsWith("--") ? args[index][2..] : throw new ArgumentException("Unexpected argument: " + args[index]);
    if (key is "publish-host" or "verify" or "plan") options.Add(key, "true");
    else { if (++index >= args.Length) throw new ArgumentException("Missing value for " + key); options.Add(key, args[index]); }
}
var allowed = new[] { "publish-host", "verify", "plan", "engine-root", "configuration", "architecture", "target-type", "framework", "backend" };
if (options.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Unknown publisher option.");
ManagedBuildPlan plan = ManagedBuildPlan.Create(root, args[0], options);
string destination = plan.IsEditor ? Path.Combine(root, "Binaries/Managed", plan.TargetFramework) : Path.Combine(root, "Content/Managed", plan.Platform);
if (options.ContainsKey("plan")) { Console.WriteLine(JsonSerializer.Serialize(new { plan, destination }, new JsonSerializerOptions { WriteIndented = true })); return; }
if (!options.ContainsKey("verify"))
{
    string engine = options.GetValueOrDefault("engine-root") ?? Environment.GetEnvironmentVariable("UE_ENGINE_ROOT") ?? throw new ArgumentException("--engine-root is required for the standard UAT build.");
    if (Directory.Exists(Path.Combine(engine, "Engine/Build"))) engine = Path.Combine(engine, "Engine");
    string uat = Path.Combine(Path.GetFullPath(engine), "Build/BatchFiles", OperatingSystem.IsWindows() ? "RunUAT.bat" : "RunUAT.sh");
    if (plan.IsEditor)
    {
        // Target switches can invalidate generated output DLLs while their UHT
        // source stays unchanged. Prepare the normal glue before user projects.
        Run(uat, ["BuildUserGlue", "-Project=" + Path.Combine(root, "SharpDemo.uproject"),
            "-ScriptDir=" + Path.Combine(root, "Plugins/UnrealSharp/Build/Scripts"),
            "-TargetType=Editor", "-TargetConfiguration=" + plan.Configuration,
            "-OutputDirectory=" + destination], root);
    }
    var command = new List<string> { plan.IsEditor ? "BuildUserSolution" : plan.IsMobile ? "PackageProjectMobile" : "PackageProject", "-Project=" + Path.Combine(root, "SharpDemo.uproject"), "-ScriptDir=" + Path.Combine(root, "Plugins/UnrealSharp/Build/Scripts") };
    if (plan.IsEditor) command.AddRange(["-OutputPath=" + destination, "-TargetConfiguration=" + plan.Configuration]);
    else
    {
        command.AddRange(["-TargetPlatform=" + plan.Platform, "-TargetArchitecture=" + plan.Architecture, "-UETargetType=" + plan.TargetType, "-UEBuildConfig=" + plan.Configuration]);
        if (!plan.IsMobile) command.Add("-ArchiveDirectory=" + Path.Combine(root, "Content/Managed", plan.Platform));
    }
    Run(uat, command, root);
    // Desktop UAT publishes its normal Binaries/Managed layout inside the archive.
    if (!plan.IsEditor && !plan.IsMobile) destination = Path.Combine(destination, "Binaries/Managed", plan.TargetFramework);
}
else if (!plan.IsEditor && !plan.IsMobile) destination = Path.Combine(destination, "Binaries/Managed", plan.TargetFramework);
ManagedPackage.Verify(destination, plan.IsEditor);
if (!options.ContainsKey("verify"))
{
    string stamps = Path.Combine(root, "Intermediate/UnrealSharp/Publish");
    Directory.CreateDirectory(stamps);
    File.WriteAllText(Path.Combine(stamps, plan.Store + ".stamp"), Guid.NewGuid().ToString());
}
Console.WriteLine("Verified ordinary UnrealSharp solution: " + destination);

static void Run(string executable, IEnumerable<string> arguments, string directory)
{
    string[] values = arguments.ToArray();
    var process = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
    foreach (string key in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBuildToolsPath" }) process.Environment.Remove(key);
    if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Equals(".bat", StringComparison.OrdinalIgnoreCase))
    {
        string Quote(string value)
        {
            if (value.IndexOfAny(['"', '%', '!', '\r', '\n', '^']) >= 0) throw new ArgumentException("Unsupported batch argument.");
            return "\"" + value + "\"";
        }
        process.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        process.Arguments = "/d /s /c \"" + string.Join(" ", values.Prepend(executable).Select(Quote)) + "\"";
    }
    else foreach (string argument in values) process.ArgumentList.Add(argument);
    using var child = Process.Start(process) ?? throw new InvalidOperationException("Could not start " + executable);
    child.WaitForExit();
    if (child.ExitCode != 0) throw new InvalidOperationException("Managed build failed (" + child.ExitCode + ").");
}
