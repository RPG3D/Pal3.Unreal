# PR: Normalize hostfxr paths only on Windows

## Summary

`FCSDotNetRuntimeHost::ConfigureRuntime` passes `dotnet_root`, `host_path` and
the runtime config path to hostfxr. Two platform behaviours conflict:

- **Windows:** hostfxr/coreclr reject forward slashes. `coreclr_initialize`
  fails with `E_INVALIDARG`, which surfaces as `0x80008089` from
  `hostfxr_get_runtime_delegate`.
- **Unix (macOS/Linux):** the host requires forward slashes. Replacing them with
  `\` produces an unreadable `dotnet_root`, and hostfxr reports
  `No frameworks were found` (`0x80008096`) — which is what a cook on macOS did
  before this change.

The fix normalizes separators only on Windows and leaves the Unix path untouched.

```cpp
// FPaths can hand forward-slash paths to hostfxr, which rejects them on
// Windows (coreclr_initialize fails with E_INVALIDARG, surfacing as
// 0x80008089 from hostfxr_get_runtime_delegate). Unix hosts require forward
// slashes: converting them to '\' makes dotnet_root unreadable and hostfxr
// reports "No frameworks were found".
#if PLATFORM_WINDOWS
    const FString DotNetRootNative = Layout.DotNetRoot.Replace(TEXT("/"), TEXT("\\"));
    const FString RuntimeConfigNative = Layout.RuntimeConfigPath.Replace(TEXT("/"), TEXT("\\"));
    const FString HostPathNative = FString(FPlatformProcess::ExecutablePath()).Replace(TEXT("/"), TEXT("\\"));
#else
    const FString DotNetRootNative = Layout.DotNetRoot;
    const FString RuntimeConfigNative = Layout.RuntimeConfigPath;
    const FString HostPathNative = FString(FPlatformProcess::ExecutablePath());
#endif
```

The casts below then use these native strings instead of the raw layout fields.

## Note on scope

This PR intentionally contains **only** the separator change. The other half of
the same local commit (`reinterpret_cast<const char_t*>` for
`StringCast<UTF8CHAR>` results under C++20) is already in `main` as
`901cfce5 Fix hostfxr character pointer conversions on macOS`, so it is not
repeated here.

Our own branch additionally routes `InitializeManagedRuntime` to a raw CoreCLR
host on Android/iOS; that routing is not part of this PR. The hunk above applies
to the desktop path in the current upstream file.

## Testing

- Windows editor: runtime initializes and assemblies load.
- macOS editor + cook: no more `No frameworks were found`.
- Android/iOS use a different host path and are unaffected.
