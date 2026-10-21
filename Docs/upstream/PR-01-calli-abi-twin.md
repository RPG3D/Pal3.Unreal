# PR: Route native callbacks through an ABI twin so interpreted `calli` works

> Background and source-level proof: see `PR-01-MECHANISM.md` (JIT vs interpreter,
> .NET 10 vs .NET 11, with file/line citations and a local experiment).

## Summary

`NativeCallbacksWrapperGenerator` binds managed function pointers to native
functions (`delegate* unmanaged<...>`) and invokes them with `calli`. On a
CoreCLR build running the **interpreter** (mobile / iOS, where there is no JIT),
any `calli` whose signature requires P/Invoke marshalling is rejected at compile
time of the method:

```
InterpCompiler::EmitCalli -> "PInvoke marshalling for calli is not supported in interpreted code"
```

The method then fails with `InvalidProgramException` on first use. In
`dllimport.cpp`, `PInvoke::MarshalingRequired` has no `ELEMENT_TYPE_BYREF` case
and treats `ELEMENT_TYPE_BOOLEAN` as marshalled whenever runtime marshalling is
enabled, so every binding with a `string`, a `ref`/`out` parameter, or a `bool`
is affected. The first failures we hit were `Bind_FName.StringToName` and
`Bind_UScriptStruct.NativeCopy`.

This change keeps every public wrapper signature and every call site unchanged,
and reinterprets the bound function pointer as an **ABI-equivalent twin**:

| Managed signature | ABI twin | Conversion at the wrapper boundary |
| --- | --- | --- |
| `string` | `char*` | UTF-8 encode into a NUL-terminated buffer |
| `ref T` / `out T` | `T*` | `Unsafe.AsPointer` (`out` goes through a temporary and is assigned back) |
| `bool` | `byte` | `x ? 1 : 0` / `!= 0` |

The native side already declares these parameters as `const char*`, `T*` and C++
`bool`, so no native change is needed.

## Why this is also a correctness fix on JIT platforms

`bool` in a `delegate* unmanaged<...>` signature uses the default P/Invoke
marshalling, which is the 4-byte Win32 `BOOL`, while every bind in this plugin
uses C++ `bool` (1 byte). Parameter passing happened to work because the callee
only reads the low byte; **return values** could read undefined upper bits.
The twin's `byte` matches the native ABI exactly.

The string twin also fixes non-ASCII arguments on Windows: the CLR marshaller
uses the ANSI code page there, so the existing code in `Bind_FString.cpp`
("the runtime will marshal it as ANSI and replace non-ASCII with '?'") was
already warning about this. The native binds expect UTF-8 (`UTF8_TO_TCHAR`).

## Generated code

Before (fails on interpreted calli):

```csharp
delegate* unmanaged<IntPtr, string, IntPtr> GetNativeFunctionFromClassAndName;
```

After:

```csharp
public static IntPtr CallGetNativeFunctionFromClassAndName(IntPtr a, string b)
{
    delegate* unmanaged<IntPtr, char*, IntPtr> GetNativeFunctionFromClassAndNameAbi
        = (delegate* unmanaged<IntPtr, char*, IntPtr>)GetNativeFunctionFromClassAndName;

    int bByteCount = b != null ? Encoding.UTF8.GetByteCount(b) : 0;
    var bHeap = bByteCount > 256 ? new byte[bByteCount + 1] : null;

    fixed (byte* bHeapPtr = bHeap)
    {
        byte* bPtr = bHeapPtr;
        if (b != null && bHeap == null)
        {
            byte* bStack = stackalloc byte[bByteCount + 1];
            bPtr = bStack;
        }
        if (bByteCount > 0)
        {
            Encoding.UTF8.GetBytes(MemoryExtensions.AsSpan(b), new Span<byte>(bPtr, bByteCount));
        }
        if (b != null)
        {
            bPtr[bByteCount] = 0;
        }

        return GetNativeFunctionFromClassAndNameAbi(a, (char*)bPtr);
    }
}
```

Two details that matter:

- **NUL termination.** Native consumers read these as C strings
  (`FName(const ANSICHAR*)`, `FString` via `UTF8_TO_TCHAR`, `UE_LOGFMT`).
  `Encoding.UTF8.GetBytes` returns an exact-length array with no terminator, so
  the wrapper allocates `byteCount + 1` and writes the terminator explicitly.
- **Null vs empty.** `null` still maps to `nullptr`; an empty string maps to a
  pointer to `"\0"`. Native code that distinguishes the two keeps working.

Performance: the common case (names, paths, property names — all well under 256
bytes) no longer allocates at all. Longer strings allocate one pinned array,
which is what the CLR marshaller did before.

## Binding lookup is unaffected

`NativeBinds.TryGetBoundFunction` is keyed by the parameter size that the native
`BIND_UNREALSHARP_FUNCTION` macro computes with `GetFunctionSize`. The twin keeps
the sizes identical: `string`/`char*` and `ref T`/`T*` are both pointer-sized,
`bool`/`byte` are both 1. No registration or lookup change is needed.

## Diagnostics

A new warning (`US0015`) is reported for shapes the twin cannot express: a
`string` return value, or a `string` in a `ref`/`out` position. Today no bind
uses these shapes; the diagnostic prevents silent regressions on mobile if one is
added later. Such a bind would otherwise compile fine on desktop and throw
`InvalidProgramException` at runtime on iOS.

## Testing

- iPadOS 27.2, CoreCLR interpreter: runtime initializes, 190 assemblies load,
  `FName`/`FKey`/`Keys` construct, the managed game mode runs, mesh and
  AnimBlueprint render.
- Android 16, CoreCLR JIT: APK runs with the new glue, game mode resolves, no
  exceptions in logcat.
- macOS editor: full managed solution + UBT build clean.

## Files changed

- `Managed/UnrealSharp/UnrealSharp.SourceGenerators/NativeCallbacksWrapperGenerator.cs`
- `Managed/UnrealSharp/UnrealSharp.SourceGenerators/AnalyzerReleases.Unshipped.md`

## Alternatives considered

- **Change the binds to `char*`/`T*`/`byte` directly.** This is the same ABI but
  would touch every caller; the generator keeps the public surface stable.
- **Force `DOTNET_Interpreter` and rely on the JIT path.** Not possible: iOS has
  no JIT (W^X), and the interpreter restriction is by design.
- **Disable runtime marshalling (`DisableRuntimeMarshalling`).** It changes
  marshalling globally and does not make `calli` with `string` legal.
