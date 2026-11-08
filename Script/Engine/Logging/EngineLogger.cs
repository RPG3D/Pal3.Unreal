// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Engine.Logging
{
    using System.IO;
    using System.Runtime.CompilerServices;

    public static class EngineLogger
    {
        public static void Log(string message, [CallerFilePath] string callerFilePath = "")
        {
            string callerClassName = Path.GetFileNameWithoutExtension(callerFilePath);
            string line = $"[{callerClassName}] {message}";
            UnityEngine.Debug.Log(line);

            // Story dialogue is dispatched as a command; surface its text on the debug
            // overlay (the UMG layer does not exist yet). Everything else is log noise
            // from the command stream and is dropped there.
            if (message.Contains("DialogueRenderText", System.StringComparison.Ordinal))
            {
                string dialogue = ExtractDialogueText(message);
                if (!string.IsNullOrEmpty(dialogue))
                {
                    UnityEngine.Debug.PushDialogue(dialogue);
                }
            }
        }

        private static string ExtractDialogueText(string message)
        {
            const string Marker = "\"DialogueText\":\"";
            int start = message.IndexOf(Marker, System.StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += Marker.Length;
            int end = message.IndexOf("\"}", start, System.StringComparison.Ordinal);
            if (end < 0) return message.Substring(start);
            // Script text encodes line breaks as a literal backslash-n; the overlay
            // draws single lines, so collapse them into spaces.
            return message.Substring(start, end - start)
                .Replace("\\n", " ")
                .Replace("\\r", "")
                .Replace("\\", "");
        }

        public static void LogWarning(string message, [CallerFilePath] string callerFilePath = "")
        {
            string callerClassName = Path.GetFileNameWithoutExtension(callerFilePath);
            UnityEngine.Debug.LogWarning($"[{callerClassName}] {message}");
        }

        public static void LogError(string message, [CallerFilePath] string callerFilePath = "")
        {
            string callerClassName = Path.GetFileNameWithoutExtension(callerFilePath);
            UnityEngine.Debug.LogError($"[{callerClassName}] {message}");
        }

        public static void LogException(System.Exception exception,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            string callerClassName = Path.GetFileNameWithoutExtension(callerFilePath);
            UnityEngine.Debug.LogError($"[{callerClassName}] [{callerMemberName}] Exception: {exception.Message}");
            UnityEngine.Debug.LogException(exception);
        }
    }
}