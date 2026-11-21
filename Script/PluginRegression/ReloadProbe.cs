using UnrealSharp.Attributes;
using UnrealSharp.Engine;

namespace PluginRegression;

// Two ordinary projects share this directory/source to verify directory watching,
// native reflected-type registration and function rebinding without game code.
[UClass]
#if SECOND_PROBE
public partial class ASecondReloadProbe : AActor
#else
public partial class AReloadProbe : AActor
#endif
{
    [UFunction(FunctionFlags.BlueprintCallable)]
    public string GetRevision() => "revision-1";
}
