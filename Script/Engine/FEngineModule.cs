using UnrealSharp.Engine.Core.Modules;

namespace Engine.Module;
public class FEngineModule : IModuleInterface
{
    public void StartupModule() => UnityEngine.Debug.Log("[Engine] Module started");
    public void ShutdownModule() => UnityEngine.Debug.Log("[Engine] Module shutdown");
}
