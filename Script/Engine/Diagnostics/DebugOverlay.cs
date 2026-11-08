// DebugOverlay.cs — managed side of the C++ viewport panel (Pal3DebugOverlay.h).
// Routes the three things that matter during bring-up:
//   Dialogue : every UI text write (the port's dialogue managers write into the
//              stub TextMeshProUGUI/Text objects, so this catches story lines
//              without touching game code),
//   Log      : UnityEngine.Debug output + engine logger,
//   Debug    : driver heartbeat and diagnostics,
//   Error    : errors/exceptions.
// The panel itself is drawn by APal3DebugHUD (C++), which keeps rendering even when
// the managed runtime is wedged.
using UnrealSharp.SharpDemo;

namespace Engine.Diagnostics
{
    public static class DebugOverlay
    {
        private static bool _failed;

        /// <summary>Routes UnityEngine.Debug/GameEntityScriptRuntime output here once.</summary>
        public static void Install()
        {
            UnityEngine.Debug.LogSink = Log;
            UnityEngine.Debug.ErrorSink = Error;
            UnityEngine.Debug.DialogueSink = Dialogue;
            UnityEngine.Debug.DebugSink = Info;
        }

        public static void Dialogue(string text) => Push(EPal3OverlayCategory.Dialogue, text);
        public static void Log(string text) => Push(EPal3OverlayCategory.Log, text);
        public static void Info(string text) => Push(EPal3OverlayCategory.Debug, text);
        public static void Error(string text) => Push(EPal3OverlayCategory.Error, text);

        /// <summary>
        /// Clears the C++ overlay buffers and RC flags for a new PIE session. Without
        /// this the HUD keeps showing the previous session's dialogue line (and the
        /// boot menu state) on top of the freshly started world.
        /// </summary>
        public static void ResetForNewSession()
        {
            _failed = false;
            try { UnrealSharp.SharpDemo.UPal3DebugOverlay.Clear(); }
            catch (System.Exception) { }
            SetBootMenuVisible(false);
            SetOverlayVisible(false);
            AutoDialogueEnabled = false;
            ConsumeStartGameRequest();
            ConsumeAdvanceDialogueRequest();
        }

        // --- RC-driven loop control (state lives in the C++ overlay, polled here) ---

        /// <summary>HUD-drawn boot menu visibility.</summary>
        public static void SetBootMenuVisible(bool visible)
        {
            try { UnrealSharp.SharpDemo.UPal3DebugOverlay.BootMenuVisible = visible; }
            catch (System.Exception) { }
        }

        /// <summary>Boot menu button rect in viewport pixels.</summary>
        public static void GetBootMenuButtonRect(out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            try { UnrealSharp.SharpDemo.UPal3DebugOverlay.GetBootMenuButtonRect(out x, out y, out w, out h); }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Side diagnostics panel visibility. Off by default so the viewport shows the
        /// game; F1 (driver) or RC toggles it for debugging.
        /// </summary>
        public static void SetOverlayVisible(bool visible)
        {
            // UHT turns SetOverlayVisible(bool) into a property (like BootMenuVisible).
            try { UnrealSharp.SharpDemo.UPal3DebugOverlay.OverlayVisible = visible; }
            catch (System.Exception) { }
        }

        public static bool IsOverlayVisible
        {
            get
            {
                try { return UnrealSharp.SharpDemo.UPal3DebugOverlay.IsOverlayVisible(); }
                catch (System.Exception) { return false; }
            }
        }

        /// <summary>Clears only the dialogue lines (HUD text box hides).</summary>
        public static void ClearDialogue()
        {
            try { UnrealSharp.SharpDemo.UPal3DebugOverlay.ClearDialogue(); }
            catch (System.Exception) { }
        }

        /// <summary>Consumes an RC request to show the JSON→UMG demo widget.</summary>
        public static bool ConsumeJsonDemoRequest()
        {
            try { return UnrealSharp.SharpDemo.UPal3DebugOverlay.ConsumeJsonDemoRequest(); }
            catch (System.Exception) { return false; }
        }

        /// <summary>Consumes a pending RC "start game" request (boot menu → new game).</summary>
        public static bool ConsumeStartGameRequest()
        {
            // Not gated on _failed: Push failures must not disable RC loop control.
            try { return UPal3DebugOverlay.ConsumeStartGameRequest(); }
            catch (System.Exception) { return false; }
        }

        /// <summary>Asset pipeline diagnostics (textures created, binds).</summary>
        public static string AssetStats
        {
            get
            {
                try { return UPal3AssetHelper.AssetStats; }
                catch (System.Exception) { return "stats=n/a"; }
            }
        }

        /// <summary>Consumes a pending RC single-step dialogue advance request.</summary>
        public static bool ConsumeAdvanceDialogueRequest()
        {
            try { return UPal3DebugOverlay.ConsumeAdvanceDialogueRequest(); }
            catch (System.Exception) { return false; }
        }

        /// <summary>RC-toggleable dialogue auto-advance driver state.</summary>
        public static bool AutoDialogueEnabled
        {
            get
            {
                try { return UPal3DebugOverlay.IsAutoDialogueOn(); }
                catch (System.Exception) { return false; }
            }
            set
            {
                try { UPal3DebugOverlay.EnableAutoDialogue(value); }
                catch (System.Exception) { }
            }
        }

        public static void Push(EPal3OverlayCategory category, string text)
        {
            if (_failed || string.IsNullOrEmpty(text))
            {
                return;
            }
            if (text.Length > 240)
            {
                text = text.Substring(0, 240) + "...";
            }

            try
            {
                UPal3DebugOverlay.Push(category, text);
            }
            catch (System.Exception)
            {
                // The overlay is diagnostics-only: never let it break gameplay.
                _failed = true;
            }
        }
    }
}
