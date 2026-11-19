using UnrealSharp.SharpDemo;

namespace Engine.Diagnostics
{
    public static class DebugOverlay
    {
        private const int Capacity = 64;
        private static readonly object Gate = new();
        private static readonly Queue<string> Lines = new();
        private static int _version;

        public static bool IsOverlayVisible { get; set; }

        public static void Install()
        {
            UnityEngine.Debug.LogSink = Log;
            UnityEngine.Debug.ErrorSink = Error;
            UnityEngine.Debug.DebugSink = Info;
        }

        public static void ResetForNewSession()
        {
            lock (Gate)
            {
                Lines.Clear();
                _version++;
            }
            IsOverlayVisible = false;
        }

        public static void Log(string text) => Push("LOG", text);
        public static void Info(string text) => Push("INFO", text);
        public static void Error(string text) => Push("ERR", text);

        public static bool TryReadRecent(ref int version, int count, out string text)
        {
            lock (Gate)
            {
                text = "";
                if (version == _version) return false;
                version = _version;
                text = string.Join("\n", Lines.TakeLast(count));
                return true;
            }
        }

        public static string AssetStats
        {
            get
            {
                try { return UPal3AssetHelper.AssetStats; }
                catch (Exception) { return "stats=n/a"; }
            }
        }

        private static void Push(string category, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            const string scriptMarker = " Script [";
            int scriptStart = category == "LOG" && text.StartsWith("[PalScriptRunner] ", StringComparison.Ordinal)
                ? text.IndexOf(scriptMarker, StringComparison.Ordinal) : -1;
            if (scriptStart >= 0)
            {
                text = text[(scriptStart + scriptMarker.Length)..];
                int commandStart = text.IndexOf("]: ", StringComparison.Ordinal);
                int parametersStart = text.IndexOf(" {", StringComparison.Ordinal);
                if (commandStart >= 0 && parametersStart >= commandStart + 3)
                {
                    var command = text.AsSpan(commandStart + 3, parametersStart - commandStart - 3);
                    if (command.StartsWith("DialogueRenderText", StringComparison.Ordinal) ||
                        command.StartsWith("DialogueAddSelections", StringComparison.Ordinal))
                        text = text[..parametersStart];
                }
            }
            if (text.Length > 112) text = text[..112] + "…";
            lock (Gate)
            {
                if (Lines.Count == Capacity) Lines.Dequeue();
                Lines.Enqueue(scriptStart >= 0 ? text : $"[{category}] {text}");
                _version++;
            }
        }
    }
}
