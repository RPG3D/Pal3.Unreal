using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnrealSharp.Binds;
using UnrealSharp.Log;

// The production loader is tested unchanged; only UE's logging callback is
// supplied by this CLI test process, which does not host an Unreal Editor.
internal static unsafe class NativeTestLog
{
    public static void Initialize() => NativeBinds.Initialize((IntPtr)(delegate* unmanaged[Cdecl]<char*, char*, int, IntPtr>)&Resolve);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr Resolve(char* outer, char* name, int size) =>
        new string(outer) == "Bind_FMsg" && new string(name) == "Log"
            ? (IntPtr)(delegate* unmanaged<char*, ELogVerbosity, char*, void>)&Write : IntPtr.Zero;

    [UnmanagedCallersOnly]
    private static void Write(char* category, ELogVerbosity level, char* message) =>
        Console.WriteLine("[" + new string(category) + "] " + new string(message));
}
