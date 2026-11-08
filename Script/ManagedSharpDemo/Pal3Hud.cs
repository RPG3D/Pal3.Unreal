using Engine.Diagnostics;
using Engine.Services;
using Pal3.Game;
using Pal3.Game.Dev;
using Pal3.Game.Scene;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo
{
    internal sealed class Pal3Hud : IDisposable
    {
        private const string DiagnosticsLayout = """
        {
          "root": { "type": "canvas", "children": [
            { "type": "border", "position": [16, 16], "size": [660, 248],
              "color": [0.025, 0.035, 0.055, 0.90],
              "child": { "type": "canvas", "children": [
                { "type": "text", "text": "PAL3  /  DIAGNOSTICS   ·   TAB", "fontSize": 17,
                  "color": [0.63, 0.87, 0.77, 1], "align": "left", "position": [14, 10], "size": [630, 28] },
                { "type": "text", "id": "lines", "fontSize": 13, "align": "left",
                  "position": [14, 42], "size": [630, 190] }
              ]}
            }
          ]}
        }
        """;

        private const string DialogueLayout = """
        {
          "root": { "type": "canvas", "children": [
            { "type": "border", "anchor": "bottom_center", "position": [0, -24], "size": [840, 168],
              "color": [0.035, 0.045, 0.07, 0.94],
              "child": { "type": "canvas", "children": [
                { "type": "image", "id": "leftPortrait", "position": [12, 10], "size": [142, 146] },
                { "type": "image", "id": "rightPortrait", "position": [686, 10], "size": [142, 146] },
                { "type": "text", "id": "plain", "fontSize": 23, "align": "left", "wrap": true,
                  "color": [0.98, 0.93, 0.81, 1], "position": [28, 17], "size": [780, 118] },
                { "type": "text", "id": "withLeft", "fontSize": 23, "align": "left", "wrap": true,
                  "color": [0.98, 0.93, 0.81, 1], "position": [167, 17], "size": [645, 118] },
                { "type": "text", "id": "withRight", "fontSize": 23, "align": "left", "wrap": true,
                  "color": [0.98, 0.93, 0.81, 1], "position": [28, 17], "size": [645, 118] },
                { "type": "text", "text": "空格 / 回车继续", "fontSize": 15, "align": "left",
                  "color": [0.64, 0.65, 0.72, 1], "position": [550, 135], "size": [132, 24] }
              ]}
            }
          ]}
        }
        """;

        private URuntimeJsonWidget? _diagnostics;
        private URuntimeJsonWidget? _dialogue;
        private URuntimeJsonWidget? _loading;
        private string? _loadingText;
        private float _loadingProgress = -1f;
        private int _logVersion = -1;
        private float _diagnosticsTimer;
        private string _lastDialogue = "";
        private DialoguePresentation.Portrait? _lastPortrait;
        private bool _failed;

        public void Tick(float deltaTime)
        {
            if (_failed) return;
            try
            {
                UpdateLoading();
                UpdateDiagnostics(deltaTime);
                UpdateDialogue();
            }
            catch (Exception e)
            {
                _failed = true;
                UnrealSharp.Log.UnrealLogger.LogError("Pal3", "[PAL3 HUD] " + e);
            }
        }

        private void UpdateLoading()
        {
            var scene = Pal3Boot.IsGameReady ? ServiceLocator.Instance.Get<SceneManager>() : null;
            if (scene is not { IsLoading: true })
            {
                if (_loading != null) SetVisible(_loading, false);
                return;
            }

            if (_loading == null)
            {
                _loading = Pal3UiLayout.Create("Pal3Loading", 120);
                if (_loading == null) return;
                DebugOverlay.Info("[PAL3 loading] widget ready");
            }

            SetVisible(_loading, true);
            if (_loadingText != scene.LoadingPhase)
            {
                _loadingText = scene.LoadingPhase;
                _loading.SetElementText("loading_phase", _loadingText);
            }
            if (_loadingProgress != scene.LoadingProgress)
            {
                _loadingProgress = scene.LoadingProgress;
                _loading.SetElementProgress("loading_progress", _loadingProgress);
            }
        }

        private void UpdateDiagnostics(float deltaTime)
        {
            bool visible = DebugOverlay.IsOverlayVisible;
            if (_diagnostics == null)
            {
                if (!visible) return;
                _diagnostics = URuntimeJsonWidgetLibrary.CreatePassiveJsonWidget(DiagnosticsLayout, 70);
                if (_diagnostics == null) return;
                _diagnosticsTimer = 0;
            }

            SetVisible(_diagnostics, visible);
            if (!visible) return;

            _diagnosticsTimer -= deltaTime;
            if (_diagnosticsTimer > 0f) return;
            _diagnosticsTimer = 0.25f;
            if (DebugOverlay.TryReadRecent(ref _logVersion, 10, out string lines))
            {
                _diagnostics.SetElementText("lines", lines);
            }
        }

        private void UpdateDialogue()
        {
            string text = DialoguePresentation.Text;
            if (text.Length == 0)
            {
                if (_dialogue != null && _lastDialogue.Length != 0)
                {
                    SetVisible(_dialogue, false);
                    UpdatePortrait("leftPortrait", null);
                    UpdatePortrait("rightPortrait", null);
                }
                _lastDialogue = "";
                _lastPortrait = null;
                return;
            }

            _dialogue ??= URuntimeJsonWidgetLibrary.CreatePassiveJsonWidget(DialogueLayout, 60);
            if (_dialogue == null) return;

            var portrait = DialoguePresentation.Avatar;
            if (_lastDialogue == text && _lastPortrait == portrait) return;
            _lastDialogue = text;
            _lastPortrait = portrait;

            string textId = portrait == null ? "plain" : portrait.RightAligned ? "withRight" : "withLeft";
            _dialogue.SetElementVisible("plain", textId == "plain");
            _dialogue.SetElementVisible("withLeft", textId == "withLeft");
            _dialogue.SetElementVisible("withRight", textId == "withRight");
            _dialogue.SetElementText(textId, text);
            UpdatePortrait("leftPortrait", portrait is { RightAligned: false } ? portrait : null);
            UpdatePortrait("rightPortrait", portrait is { RightAligned: true } ? portrait : null);
            SetVisible(_dialogue, true);
        }

        private void UpdatePortrait(string id, DialoguePresentation.Portrait? portrait)
        {
            if (_dialogue == null) return;
            _dialogue.SetElementVisible(id, portrait != null);
            if (portrait == null)
            {
                _dialogue.SetElementImage(id, null!);
                return;
            }
            _dialogue.SetElementImage(id, portrait.Texture, portrait.U0, portrait.V0, portrait.U1, portrait.V1);
        }

        private static void SetVisible(URuntimeJsonWidget widget, bool visible)
        {
            var visibility = visible ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed;
            if (widget.Visibility != visibility) widget.Visibility = visibility;
        }

        public void Dispose()
        {
            if (_diagnostics != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_diagnostics);
            if (_dialogue != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_dialogue);
            if (_loading != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_loading);
            _diagnostics = null;
            _dialogue = null;
            _loading = null;
        }
    }
}
