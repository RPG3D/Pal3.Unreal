# Mechanism: why unmanaged `calli` needs an ABI twin on CoreCLR mobile

This document explains, with source citations from both .NET 10 and .NET 11, why
`NativeCallbacksWrapperGenerator` must rewrite `string` / `ref` / `out` / `bool`
in native callback signatures before the plugin can run on a CoreCLR build that
uses the **interpreter** (Apple mobile), and why the same code has worked on
Windows/macOS for years.

Short version:

1. `delegate* unmanaged<...>` is compiled to `calli` with an unmanaged calling
   convention.
2. On a build with the JIT, the runtime turns such a `calli` into a helper call
   (`CORINFO_HELP_PINVOKE_CALLI`) that runs a lazily generated **IL marshalling
   stub**. That stub is what converts `string` to `char*`, `bool` to a 4-byte
   `BOOL`, and passes `ref`/`out` as pointers.
3. On the interpreter there is no such stub — the interpreter emits a raw call
   stub from the signature. .NET 11 therefore rejects the call site at compile
   time; .NET 10 did not reject it and silently passed managed values.
4. iOS/tvOS builds disable dynamic code compilation by default, so the
   interpreter *is* the execution engine there.

## 1. What the generator emits

The bind classes declare a function pointer for every native entry point, e.g.
`Bind_FProperty.cs`:

```csharp
public static delegate* unmanaged<IntPtr, string, int> GetPropertyOffsetFromName;
```

and the generated wrapper calls it. The native side expects
`int GetPropertyOffsetFromName(const FProperty*, const char*)` — the plugin's
bind convention is UTF-8 `const char*` (`Bind_FString.cpp` uses
`UTF8_TO_TCHAR`, `FName(const ANSICHAR*)` is null-terminated).

## 2. JIT: the runtime inserts a marshalling stub

The JIT special-cases unmanaged `calli`:

- `src/coreclr/jit/importercalls.cpp:939-951` — for `CALLI` with a non-default,
  non-vararg calling convention, the JIT requests a cookie from
  `GetCookieForPInvokeCalliSig`.
- `src/coreclr/jit/morph.cpp:1759-1785` — the cookie is passed as the
  `PInvokeCookie` well-known argument and the call is rewritten to the
  `CORINFO_HELP_PINVOKE_CALLI` helper with the target in a second well-known
  argument.
- `src/coreclr/vm/dllimport.cpp:6183-6207` — the helper's worker
  (`GenericPInvokeCalliStubWorker`) calls `GetILStubForCalli`
  (`dllimport.cpp:6037`), which builds a P/Invoke IL stub for the call-site
  signature. The stub marshals each argument according to the signature and
  `MarshalAs` rules (the same `MarshalInfo` machinery a `[DllImport]` uses).

That is why `string`, `bool` and `ref` parameters work on Windows/macOS: the
managed values never reach the native function directly; the IL stub marshals
them.

**Empirical check** (macOS, x64 JIT, both runtimes installed locally):

```csharp
IntPtr lib = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
IntPtr strlenPtr = NativeLibrary.GetExport(lib, "strlen");

var strlenString = (delegate* unmanaged[Cdecl]<string, nuint>)strlenPtr;
Console.WriteLine(strlenString("hello"));      // 5 => marshalled to UTF-8/ANSI char*
```

Result:

```
.NET 10.0.13                  strlen(string "hello") = 5
.NET 11.0.0-rc.2.26504.105   strlen(string "hello") = 5
```

A raw managed string reference would not produce 5 (a `System.String` is
UTF-16 with a length field; a C `strlen` would return 1 or garbage).

## 3. Interpreter: no marshalling stub, and .NET 11 rejects the call site

### .NET 11 (`release/11.0`, local clone @ `3beced8c`)

`src/coreclr/interpreter/compiler.cpp:4502-4523`:

```cpp
void InterpCompiler::EmitCalli(bool isTailCall, void* calliCookie, int callIFunctionPointerVar, CORINFO_SIG_INFO* callSiteSig)
{
    ...
    bool isPInvoke = (callConv != CORINFO_CALLCONV_DEFAULT && callConv != CORINFO_CALLCONV_VARARG);
    if (isPInvoke)
    {
        if (m_compHnd->pInvokeMarshalingRequired(NULL, callSiteSig))
        {
            // If we remove this restriction, we should handle the track transitions scenario by forcing a
            // p/invoke marshaling calli stub even when not needed.
            BADCODE("PInvoke marshalling for calli is not supported in interpreted code");
        }
        m_compHnd->getUnmanagedCallConv(nullptr, callSiteSig, &suppressGCTransition);
    }
    ...
}
```

`BADCODE` fails method compilation, which surfaces as `InvalidProgramException`
the first time the method is executed. The comment is explicit: making this work
would require forcing a P/Invoke marshalling calli stub, which the interpreter
does not do.

The check is driven by `PInvoke::MarshalingRequired`
(`src/coreclr/vm/dllimport.cpp:3305`, reached from
`CEEInfo::pInvokeMarshalingRequired`, `jitinterface.cpp:10124`), and for our
signatures it is always true:

- `string`: `ELEMENT_TYPE_STRING` is not a primitive → falls to
  `dllimport.cpp:3522` "other non-primitive type - requires marshaling" →
  `TRUE`.
- `ref`/`out`: there is no `ELEMENT_TYPE_BYREF` case at all, so it takes the
  same default → `TRUE` (this is why even `ref int` is rejected).
- `bool`: `dllimport.cpp:3495-3501` returns `TRUE` whenever runtime marshalling
  is enabled, because the contract is a 4-byte Win32 `BOOL`.

Execution side: `src/coreclr/vm/interpexec.cpp:3289-3320` (`INTOP_CALLI`) calls
`InvokeUnmanagedCalli` / `InvokeUnmanagedCalliWithTransition`, which use the
native call stub created by `CallStubGenerator::GenerateCallStubForSig`. That
stub only lays arguments out according to the unmanaged ABI (`ArgIterator`); it
never runs the IL marshalling stub. Searching the interpreter for
`GetILStubForCalli` or `CORINFO_HELP_PINVOKE_CALLI` returns nothing.

### .NET 10 (`release/10.0`, local clone @ `a3788b7`)

There is no `EmitCalli` and no such check. `src/coreclr/interpreter/compiler.cpp`
emits `INTOP_CALLI` directly (lines 3040, 3094, 3129), and
`src/coreclr/vm/interpexec.cpp:1879-1890` executes it through
`InvokeCalliStub(..., pCallStub, ...)` — the same raw native call stub, again
without any marshalling stub.

So on .NET 10 the interpreted call does **not** throw, but it also does **not**
marshal: a `string` argument is passed as a managed object reference (for
`ELEMENT_TYPE_STRING` the interpreter's argument layout is a pointer-sized
value holding the object reference), and `bool` is whatever the ABI layout
produces instead of the 4-byte `BOOL` the marshalling contract assumes. In other
words, .NET 11 turned a silent wrong-argument bug into a loud
`InvalidProgramException`; it did not introduce a new requirement.

## 4. Why Apple mobile always runs the interpreter

- `src/coreclr/eng/build.sh:589-596` — `dynamiccodecompiled` defaults to
  `false` for `ios`, `iossimulator`, `tvos`, `tvossimulator`, `maccatalyst`,
  `browser`, `wasi`.
- `src/coreclr/clr.featuredefines.props:60-63` — Apple mobile targets force
  `FeatureInterpreter=true` (this is new in .NET 11; .NET 10 only enabled the
  interpreter for desktop Debug/Checked builds, `clr.featuredefines.props:31`).
- `src/coreclr/vm/eeconfig.cpp:452-470` — with `FEATURE_DYNAMIC_CODE_COMPILED`
  *not* defined, `enableInterpreter = true` unconditionally. (With it defined,
  the interpreter is opt-in via `DOTNET_Interpreter` / `InterpMode`. That is
  why our host's `setenv("DOTNET_Interpreter", "1")` was unnecessary for the
  iOS SDK and was removed.)
- `src/coreclr/vm/codeman.cpp:3938-3956` — the interpreter is loaded once
  enabled; with `FEATURE_STATICALLY_LINKED && !FEATURE_DYNAMIC_CODE_COMPILED`
  it is linked in statically (`InitializeStaticJIT`).

On an iOS device the process cannot allocate executable memory for a JIT, so the
interpreter is the only engine that can run IL. This is not a signing detail:
the default iOS build already compiles out dynamic code.

## 5. Matrix

| Target / build | Engine | `string` calli | `ref`/`out` calli | `bool` calli | Result with the old generator |
| --- | --- | --- | --- | --- | --- |
| Windows / macOS / Linux (JIT) | JIT + P/Invoke calli stub | marshalled (ANSI; UTF-8 on Unix) | pointer | 4-byte `BOOL` | works (with ANSI caveats on Windows) |
| Android (JIT) | JIT | marshalled | pointer | `BOOL` | works |
| iOS / tvOS device, .NET 10 interpreter | interpreter, raw call stub | **not marshalled** | pointer | raw | silent wrong arguments |
| iOS / tvOS device, .NET 11 interpreter | interpreter, raw call stub | — | — | — | `InvalidProgramException` (`BADCODE`) |
| WASM / wasi | interpreter | — | — | — | same as iOS 11 |

Note the `string` column for the JIT: Windows uses the ANSI code page, so
non-ASCII names are replaced with `?` — the native side expects UTF-8
(`Bind_FString.cpp` even documents this). The ABI twin fixes that too by always
encoding UTF-8.

## 6. Why the fix must be in the generator, not at the call sites

The ABI twin keeps the public surface intact:

- **Call sites unchanged.** 18 bind declarations use `string`; changing them to
  `char*` would push UTF-8 encoding into every caller and lose type safety.
- **Registry lookup unchanged.** `NativeBinds.TryGetBoundFunction` matches on
  the parameter size computed by `BIND_UNREALSHARP_FUNCTION`
  (`CSBindsRegistry.h:42-47`). The twin preserves every size: `string`/`char*`
  and `ref T`/`T*` are pointer-sized, `bool`/`byte` are 1 byte.
- **Native side unchanged.** The binds already take `const char*`, `T*` and C++
  `bool`; the twin is the exact ABI they expect.
- **Correct on JIT too.** The twin's `byte` for `bool` matches C++ `bool`
  (1 byte) more precisely than the marshaller's 4-byte `BOOL`, and explicit
  UTF-8 removes the ANSI dependency.

Alternatives that were considered and do not work:

- `[assembly: DisableRuntimeMarshalling]` makes `bool` blittable (1 byte), but
  `string` is still not a primitive and `BYREF` still has no case in
  `MarshalingRequired`, so those calli remain "requires marshalling" and the
  interpreter still rejects them (`dllimport.cpp:3495-3523`).
- Setting `DOTNET_Interpreter`/`InterpMode` does nothing for an SDK that has no
  dynamic code (the interpreter is already the only engine).
- `[UnmanagedCallersOnly]` does not apply: these are indirect calls through
  function pointers, not reverse P/Invoke.

## 7. Verification performed

- iPadOS 27.2, CoreCLR interpreter: runtime initializes, 190 assemblies load,
  `FName`/`FKey`/`Keys` construct, the managed game mode runs, mesh and
  AnimBlueprint render, no `InvalidProgramException`.
- Android 16, CoreCLR JIT: same build of the plugin runs, game mode resolves,
  no exceptions in logcat.
- Local JIT experiment above (net10.0 and net11.0, macOS x64).
- Generated code inspected for 1/2/3 string parameters, `ref`/`out`, and `bool`
  return/parameter shapes; plugin and game solutions build with 0 errors.

## 8. Caveats and limits of the twin

- `string` is encoded as NUL-terminated UTF-8. `Encoding.UTF8.GetBytes` does not
  append a terminator, so the wrapper writes one explicitly; without it native
  `FName`/`FString`/logging would read past the buffer.
- Short strings (<= 256 UTF-8 bytes) use `stackalloc` and allocate nothing;
  longer ones use one pinned `byte[]`.
- A `string` return value or a `ref`/`out string` has no twin yet; a build-time
  warning (`US0015`) reports those shapes instead of failing at runtime.
- `ref`/`out` arguments are passed as raw pointers, exactly like `ref T` under
  the previous JIT marshaller. They are not pinned beyond the call; all current
  call sites pass locals or unmanaged structs during startup.

## Appendix: source locations

.NET 11, `release/11.0`:

| What | Location |
| --- | --- |
| Interpreter rejects marshalled calli | `src/coreclr/interpreter/compiler.cpp:4502-4523` |
| `pInvokeMarshalingRequired` | `src/coreclr/vm/jitinterface.cpp:10124-10145` |
| `MarshalingRequired` implementation | `src/coreclr/vm/dllimport.cpp:3305` |
| `bool` requires marshalling | `src/coreclr/vm/dllimport.cpp:3495-3501` |
| Non-primitive (string/BYREF) requires marshalling | `src/coreclr/vm/dllimport.cpp:3520-3523` |
| JIT calli cookie | `src/coreclr/jit/importercalls.cpp:939-951` |
| Helper rewrite | `src/coreclr/jit/morph.cpp:1759-1785` |
| P/Invoke calli IL stub | `src/coreclr/vm/dllimport.cpp:6037`, `6183-6207` |
| Interpreter calli execution | `src/coreclr/vm/interpexec.cpp:3289-3320` |
| Interpreter enablement | `src/coreclr/vm/eeconfig.cpp:452-470` |
| Apple mobile forces interpreter | `src/coreclr/clr.featuredefines.props:60-63` |
| iOS default: no dynamic code | `eng/build.sh:589-596` |
| Static linkage when no dynamic code | `src/coreclr/clrfeatures.cmake:18`, `src/coreclr/vm/codeman.cpp:3938-3956` |

.NET 10, `release/10.0`:

| What | Location |
| --- | --- |
| Interpreter emits calli with no check | `src/coreclr/interpreter/compiler.cpp:3040, 3094, 3129` |
| Interpreter calli execution (raw stub) | `src/coreclr/vm/interpexec.cpp:1879-1890` |
| `MarshalingRequired` implementation | `src/coreclr/vm/dllimport.cpp:3174` |
| `bool` requires marshalling | `src/coreclr/vm/dllimport.cpp:3356` |
| Non-primitive requires marshalling | `src/coreclr/vm/dllimport.cpp:3383` |
| Interpreter is opt-in when JIT exists | `src/coreclr/vm/jitinterface.cpp:13324-13338` |
| `FEATURE_JIT` on all non-WASM targets | `src/coreclr/clrfeatures.cmake:1-3` |
