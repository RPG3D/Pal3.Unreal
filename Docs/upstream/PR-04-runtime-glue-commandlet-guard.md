# PR: Do not rewrite generated runtime glue during commandlets

## Summary

`UnrealSharpRuntimeGlue` is an Editor module that writes generated C# sources
(`GameplayTags.cs`, `TraceChannel.cs`, ...) into the project's `Script/<Project>.RuntimeGlue`
folder at startup. During a cook, the module starts before plugin gameplay-tag
sources and the project's collision settings are registered, so the generators
produce a **degraded subset** and overwrite the committed files.

Reproduced on a `BuildCookRun`/`Cook` commandlet (UE 5.7.4, macOS):

```
LogUnrealSharpRuntimeGlue: Display: Saved TraceChannel
LogUnrealSharpRuntimeGlue: Display: Saved GameplayTags
```

The resulting `GameplayTags.cs` lost the Niagara tags (`Niagara`,
`Niagara.ScalabilityProfile.*`), and `TraceChannel.cs` lost every
`EObjectChannel` member and `CollisionProfiles.WaterBodyCollision`. The cook log
shows the write happening during early engine startup, before the UnrealSharp
managed assemblies are loaded.

A cook never needs to regenerate these files: the RuntimeGlue assembly is built
*before* the cook (the packaging flow publishes managed code first), so the
checked-in sources are the correct input.

## Fix

Skip the write in commandlets, at the single choke point all generators use:

```cpp
void UCSGlueGenerator::SaveRuntimeGlue(FCSScriptBuilder& ScriptBuilder, const FString& FileName, const FString& Suffix)
{
    // Cooks and other commandlets start this module before plugin tag sources and the
    // project collision settings are registered, so they would overwrite the committed
    // glue with a degraded subset. The glue assembly is built before a cook.
    if (IsRunningCommandlet())
    {
        return;
    }
    ...
}
```

`IsRunningCommandlet()` comes from `CoreGlobals.h`, already included through the
module's existing dependencies.

## Why not make the generators wait for the missing data

`ProcessGameplayTags` could early-out while tag sources are incomplete, and the
collision-profile generator could wait for `OnLoadProfileConfig`. Both are larger
changes, and the correct state during a cook is still "do not write source files"
— a commandlet should not mutate the working tree. The guard also covers future
generators added to `UCSRuntimeGlueSettings`.

## Testing

- Cook (`-run=Cook`): no `Saved ...` lines; `git status` on the glue folder stays
  clean.
- Editor session: glue still refreshes on startup and via "Refresh Runtime Glue".
- Packaged builds: unchanged (they consume the already-built glue assembly).

## Files

- `Source/UnrealSharpRuntimeGlue/Private/CSGlueGenerator.cpp`
