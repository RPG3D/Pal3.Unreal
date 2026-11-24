using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ManagedModules;
using UnrealSharp.Plugins;

NativeTestLog.Initialize();
int count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or FileNotFoundException) { Check(true, message); return; }
    throw new Exception("Accepted invalid package: " + message);
}
string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "SharpDemo.uproject"))) root = Directory.GetParent(root)!.FullName;
string fixture = Path.Combine(root, "Saved/Review/OrdinaryPluginTests", Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
byte[] Compile(string name, string body)
{
    var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(body)], references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
    using var output = new MemoryStream();var result = compilation.Emit(output);
    if (!result.Success) throw new Exception(string.Join("\n", result.Diagnostics));
    return output.ToArray();
}
string Component(string name, string body)
{
    string path = Path.Combine(fixture, name + ".dll");File.WriteAllBytes(path, Compile(name, body));
    File.WriteAllText(Path.ChangeExtension(path, ".deps.json"), JsonSerializer.Serialize(new {
        runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
        targets = new Dictionary<string,object> { [".NETCoreApp,Version=v10.0"] = new Dictionary<string,object> { [name+"/1.0.0"] = new { runtime = new Dictionary<string,object> { [name+".dll"] = new {} } } } },
        libraries = new Dictionary<string,object> { [name+"/1.0.0"] = new { type = "project", serviceable = false, sha512 = "" } }
    }));
    return path;
}
string ordinary = Component("Ordinary.Portability", "public class Entry { public int Value()=>42; }");
Assembly first = PluginLoader.LoadPlugin(ordinary, true) ?? throw new Exception("Ordinary load failed");
Check((int)first.GetType("Entry")!.GetMethod("Value")!.Invoke(Activator.CreateInstance(first.GetType("Entry")!),null)! == 42, "ordinary plugin executes without an assembly-set API");
Check(PluginLoader.FindPlugin("ordinary.portability") != null, "ordinary plugin lookup respects CLR name casing");
Check(Path.GetFullPath(first.Location) == Path.GetFullPath(ordinary), "ordinary no-PDB plugin preserves its physical location");
PluginLoader.UnloadPlugin(ordinary);
Check(PluginLoader.FindPlugin("Ordinary.Portability") == null, "ordinary unload removes the plugin registry entry");
Assembly second = PluginLoader.LoadPlugin(ordinary, true) ?? throw new Exception("Ordinary reload failed");
Check(!ReferenceEquals(first,second), "ordinary reload creates a new assembly instance");
Check(AssemblyLoadContext.GetLoadContext(second)!.IsCollectible, "ordinary editor plugin retains collectible context semantics");
string broken = Component("Broken.Portability", "public static class Init { [System.Runtime.CompilerServices.ModuleInitializer] public static void Run()=>throw new System.InvalidOperationException(\"intentional regression fixture\"); }");
Check(PluginLoader.LoadPlugin(broken,true)==null, "module initializer failure is reported");
Check(PluginLoader.FindPlugin("Broken.Portability")==null, "failed initialization does not leave a falsely successful cached plugin");
Check(typeof(PluginLoader).Assembly.GetType("UnrealSharp.Plugins.ManagedAssemblySet")==null, "plugin has no project-specific assembly-set lifecycle");
var shipping=ManagedBuildPlan.Create(root,"Android",new Dictionary<string,string>{{"configuration","Shipping"}});
Check(shipping.DotNetConfiguration=="Release" && shipping.TargetType=="Game", "normal mobile plan preserves the requested build configuration");
var simulator=ManagedBuildPlan.Create(root,"IOS",new Dictionary<string,string>{{"architecture","IOSSimulator"}});
Check(simulator.RuntimeIdentifier=="iossimulator-arm64", "simulator architecture is modeled without starting a simulator");
string package=Path.Combine(fixture,"Package");Directory.CreateDirectory(package);
string[] names=ManagedPackage.GameAssemblies.Concat(["GameHost","GameHost.Contracts","ManagedSharpDemo"]).ToArray();
foreach(string name in names)File.WriteAllBytes(Path.Combine(package,name+".dll"),Compile(name,"namespace ManagedSharpDemo { public class Pal3GameModule {} }"));
string manifest=Path.Combine(package,"UserCode.LoadOrder.json");
File.WriteAllText(manifest,JsonSerializer.Serialize(new {Collectible=true,Priority=0,LoadOrder=names}));
ManagedPackage.Verify(package,true);Check(true,"both game variants use an ordinary startup manifest and normal filenames");
File.WriteAllText(manifest,JsonSerializer.Serialize(new {Collectible=false,Priority=0,LoadOrder=names}));
Reject(()=>ManagedPackage.Verify(package,true),"editor package cannot silently disable upstream unload/reload");
File.WriteAllText(manifest,JsonSerializer.Serialize(new {Collectible=true,Priority=0,LoadOrder=names.Where(n=>n!="Pal3A.Frontend")}));
Reject(()=>ManagedPackage.Verify(package,true),"missing second game is rejected by project delivery checks");
File.WriteAllText(manifest,JsonSerializer.Serialize(new {Collectible=true,Priority=0,LoadOrder=names}));
File.WriteAllBytes(Path.Combine(package,"Pal3.Core.dll"),Compile("Wrong.Identity","public class Entry {}"));
Reject(()=>ManagedPackage.Verify(package,true),"mismatched physical and CLR assembly names are rejected");
ManagedPackage.ValidateVerificationOptions(new Dictionary<string,string> { ["verify"]="true" });
Check(true,"layout verification accepts its supported parameter set");
foreach (string option in new[] { "configuration", "architecture", "target-type", "framework", "backend", "publish-host", "plan" })
{
    try
    {
        ManagedPackage.ValidateVerificationOptions(new Dictionary<string,string> { ["verify"]="true", [option]="unsupported" });
        throw new Exception("Verification accepted an uncheckable build parameter: " + option);
    }
    catch (ArgumentException) { Check(true,"layout verification rejects --" + option + " rather than implying a build-plan check"); }
}
ManagedPackage.ValidateVerificationOptions(new Dictionary<string,string> { ["configuration"]="Shipping", ["architecture"]="IOSSimulator" });
Check(true,"actual publication still accepts requested build parameters");
Console.WriteLine(count+" ordinary loader/project checks passed.");
