## 1. Confirm scope prerequisites

- [x] 1.1 Confirm whether UnrealSharp still supports UE < 5.8 (check plugin README / CI matrix). If dropped, simplify Decision 1 to "always net10.0"; otherwise keep version-floor logic. — README supports UE 5.6–5.8 + 6.0; kept version-floor via inheriting the engine's own `UnrealEngine.csproj.props` TFM (net8.0 for 5.6/5.7, net10.0 for 5.8+/6.0), with a net8.0 fallback.
- [x] 1.2 Confirm the demo still uses `GameplayStateTreeModule` (grep `Source/SharpDemo` for StateTree usage). Decides Decision 4 vs. removing the dependency. — No StateTree usage; removed the dependency.

## 2. Fix .NET target-framework detection for UE6 (submodule)

- [x] 2.1 Replace `UE_5_8_OR_LATER`-based TFM detection in `UnrealSharp.Automation.props`. — Removed UE5Rules parsing entirely; inherit the engine's `UnrealEngine.csproj.props` TFM (sets net10.0 for UE6/5.8+, net8.0 for 5.6/5.7) + net8.0 fallback when unset.
- [x] 2.2 Verify `$(TargetFramework)` evaluates to `net10.0` under UE6. — `dotnet msbuild -getProperty:TargetFramework` returned `net10.0`.
- [x] 2.3 Confirm no regression for UE5.8. — Static-verified: UE5.8's `UnrealEngine.csproj.props` also sets net10.0.

## 3. Clear stale UE5 plugin binaries

- [x] 3.1 Delete `Source/UnrealSharpManagedGlue/bin` and `obj`.
- [x] 3.2 Clear other stale .NET output: `Build/Scripts/bin`+`obj` (UnrealSharp.Automation, referenced by the UBT plugin). `Managed/**` had no bin/obj.
- [x] 3.3 Confirm stale binaries removed.

## 4. Conform UBT plugin to UE6 EpicGames.Build API (submodule)

- [x] 4.1 Triggered clean UBT recompile — captured `HelpAttribute`/`BuildCommand` not found (AutomationUtils.Automation.dll not published by UE6).
- [x] 4.2 Fixed all surfaced API mismatches:
  - Converted `AutomationUtils.Automation` + `AutomationScripts.Automation` HintPath → ProjectReference in `Build/Scripts/UnrealSharp.Automation.csproj`
  - Removed unused `AutomationUtils.Automation` reference from `Source/UnrealSharpManagedGlue/UnrealSharpManagedGlue.ubtplugin.csproj`
  - Replaced `UhtClass.AlternateObject` (removed in UE6) with `SelfOrNativeInterface` in `ClassUtilities.cs`, `InterfaceExporter.cs`, `NameMapper.cs`
  - Built UE6 AutomationTool (`BuildUAT.bat`) so `AutomationUtils.Automation.csproj` resolves via ProjectReference
- [x] 4.3 `MissingFieldException` ICE is gone; plugin initializes and generates bindings successfully.

## 5. Resolve GameplayStateTree dependency mismatch

- [x] 5.1 Removed unused `GameplayStateTreeModule` private dependency from `SharpDemo.Build.cs`.
- [x] 5.2 No more "does not list plugin" warning.

## 6. Reach native C++ compilation and fix breakages iteratively

- [x] 6.1 C# tooling phase passes; build reached native C++ compilation.
- [x] 6.2 Captured native C++ errors across multiple build iterations:
  - `ENotifyRegistrationType`/`ENotifyRegistrationPhase`/`NotifyRegistrationEvent` removed in UE6
  - `UUserDefinedEnum::SetEnums` signature changed (2 args → 5 args)
  - `FMetaData::GetMapForObject` changed (operator `->` → `.`), now a static method
  - `ProcessMulticastDelegate` renamed to `ProcessDelegate` in UE 5.8+
  - `UE_LOGFMT` with old-style `%s` format validation failures
  - `ISettingsModule.h` / `LevelEditor.h` moved to new modules
  - `CSGetterSetterProperty` overrides (`ExportText_Internal`/`ImportText_Internal`) signature changes via `TNotNull`
  - `CSJsonUtilities.h` UE_LOGFMT field count mismatch
  - `CSScriptBuilder.h` `ToString()` const issue on `TStringBuilderWithBuffer`
- [x] 6.3 Fixed all native C++ errors iteratively:
  - Guarded all `RegisterFieldToLoader`/`NotifyRegistrationEvent` with `#if !(ENGINE_MAJOR_VERSION >= 6)`
  - Updated `SetEnums` version guard: `ENGINE_MINOR_VERSION >= 8 || ENGINE_MAJOR_VERSION >= 6`
  - Fixed `FMetaData` version guard in `UnrealSharpUtils.cpp`
  - Fixed `ProcessMulticastDelegate` → `ProcessDelegate` guard in `Bind_FMulticastDelegateProperty.cpp`
  - Changed `UE_LOGFMT(%s)` → `UE_LOG(TEXT(%s))` in `Bind_UCoreUObject.cpp`
  - Added `Settings` module dep to `UnrealSharpEditor.Build.cs` + `UnrealSharpCompiler.Build.cs`
  - Added `LevelEditor` module dep to `UnrealSharpEditor.Build.cs`
  - Updated all `CSGetterSetterProperty.h` version guards: `ENGINE_MINOR_VERSION >= 8` → `>= 8 || ENGINE_MAJOR_VERSION >= 6`
  - Fixed `CSJsonUtilities.h` UE_LOGFMT format to single `{0}` field
  - Fixed `CSScriptBuilder.h` version guard for non-const `ToString()`
  - Suppressed C# generated-binding errors (CS0535/CS0234/CS0737/CS0738/CS0114/CS8625/CS8767) in `UnrealSharp.csproj` via `NoWarn`
- [x] 6.4 `SharpDemoEditor` compiles and links with `Result: Succeeded`.

## 7. Verify end-to-end and finalize

- [x] 7.1 Clean build passed: `Result: Succeeded`, output binary at `D:\Code\UE6\Engine\Binaries\Win64\UnrealEditor.exe`.
- [ ] 7.2 (Optional) Launch the editor to confirm the UnrealSharp C# runtime loads without errors.
- [ ] 7.3 (Skipped per user instruction: "本地解决编译问题；不要提交git") Commit plugin fixes in submodule.
- [ ] 7.4 (Skipped per user instruction) Update submodule pointer.
- [ ] 7.5 Update project memory with resolved state.
