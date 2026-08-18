using UnrealSharp.Engine.Core.Modules;

namespace Pal3.Game.Module;
public class FGameModule : IModuleInterface
{
    public void StartupModule() => UnityEngine.Debug.Log("[Pal3.Game] Module started");
    public void ShutdownModule() => UnityEngine.Debug.Log("[Pal3.Game] Module shutdown");
}
