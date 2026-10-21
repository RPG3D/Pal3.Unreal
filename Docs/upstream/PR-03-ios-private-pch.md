# PR: Private PCH for the iOS unity grouping

## Summary

Game-target unity builds group source files differently per platform. On iOS the
grouping can start with a different translation unit than on Android/desktop, so
files that rely on a *neighbour* having included a header fail to compile only on
iOS. Observed errors:

```
UBlueprintFunctionLibrary : undeclared identifier
FJsonValue                : undeclared identifier
UPackage / NewObject      : undeclared identifier
```

This adds an iOS-only private PCH, `Source/UnrealSharpUtilities/Public/UnrealSharpSharedPCH.h`,
and wires it into the three runtime modules:

```csharp
// UnrealSharpCore.Build.cs, UnrealSharpAsync.Build.cs, UnrealSharpUtilities.Build.cs
if (Target.Platform == UnrealTargetPlatform.IOS)
{
    PrivatePCHHeaderFile = "Public/UnrealSharpSharedPCH.h";
}
```

The header pulls in the Engine/CoreUObject surface the modules already assume
(`CoreMinimal`, Blueprint/DataTable/World/GameInstance, subsystems, Json
reader/serializer, SlateStyle, TimerManager, UObject/Object/Package/UObjectGlobals).
`UnrealSharpCore` needs the Object/Package/UObjectGlobals includes in addition to
the set used by the other two modules.

## Why a private PCH instead of more includes

Making every file self-contained is the cleaner long-term direction, but the
missing includes are spread over many files and depend on the platform-specific
unity grouping. A private PCH is what the Mono/iOS line of this fork already uses,
and it is scoped to iOS so the desktop IWYU builds are unchanged. Removing the
PCH later is a pure build-system change that does not affect runtime behaviour.

## Testing

- iOS Game target (arm64): unity build compiles; the errors above are gone.
- Android/macOS: unaffected (`Target.Platform == IOS` guard).
- The same header is already used by the Mono-based iOS build in this fork.

## Files

- `Source/UnrealSharpUtilities/Public/UnrealSharpSharedPCH.h` (new)
- `Source/UnrealSharpCore/UnrealSharpCore.Build.cs`
- `Source/UnrealSharpAsync/UnrealSharpAsync.Build.cs`
- `Source/UnrealSharpUtilities/UnrealSharpUtilities.Build.cs`
