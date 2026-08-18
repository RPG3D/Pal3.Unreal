namespace UnityEngine
{
    public static class Debug
    {
        private const string LogName = "Pal3";
        public static void Log(object msg) => UnrealSharp.Log.UnrealLogger.Log(LogName, msg?.ToString() ?? "null");
        public static void LogWarning(object msg) => UnrealSharp.Log.UnrealLogger.LogWarning(LogName, msg?.ToString() ?? "null");
        public static void LogError(object msg) => UnrealSharp.Log.UnrealLogger.LogError(LogName, msg?.ToString() ?? "null");
        public static void LogException(System.Exception ex) => UnrealSharp.Log.UnrealLogger.LogError(LogName, $"Exception: {ex}");
        public static void Assert(bool condition) { if (!condition) UnrealSharp.Log.UnrealLogger.LogError(LogName, "Assertion failed"); }
        public static void Assert(bool condition, object message) { if (!condition) UnrealSharp.Log.UnrealLogger.LogError(LogName, $"Assertion failed: {message}"); }
    }
}
