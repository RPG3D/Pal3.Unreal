namespace UnityEngine
{
    public static class Debug
    {
        private const string LogName = "Pal3";

        // Set by Engine.Diagnostics.DebugOverlay (UnityEngine must not reference
        // Engine: the dependency runs the other way). Lets log lines reach the
        // managed diagnostics panel without a circular assembly reference.
        public static System.Action<string>? LogSink;
        public static System.Action<string>? ErrorSink;
        public static System.Action<string>? DebugSink;

        /// <summary>Pushes a debug info line to the viewport overlay (set by DebugOverlay).</summary>
        public static void PushDebug(string text) => DebugSink?.Invoke(text);
        public static void Log(object msg)
        {
            string text = msg?.ToString() ?? "null";
            UnrealSharp.Log.UnrealLogger.Log(LogName, text);
            LogSink?.Invoke(text);
        }

        public static void LogWarning(object msg)
        {
            string text = msg?.ToString() ?? "null";
            UnrealSharp.Log.UnrealLogger.LogWarning(LogName, text);
            LogSink?.Invoke(text);
        }

        public static void LogError(object msg)
        {
            string text = msg?.ToString() ?? "null";
            UnrealSharp.Log.UnrealLogger.LogError(LogName, text);
            ErrorSink?.Invoke(text);
        }
        public static void LogException(System.Exception ex) => UnrealSharp.Log.UnrealLogger.LogError(LogName, $"Exception: {ex}");
        public static void Assert(bool condition) { if (!condition) UnrealSharp.Log.UnrealLogger.LogError(LogName, "Assertion failed"); }
        public static void Assert(bool condition, object message) { if (!condition) UnrealSharp.Log.UnrealLogger.LogError(LogName, $"Assertion failed: {message}"); }
    }
}
