using Engine.Diagnostics;
using Engine.Services;
using Pal3.Game;
using Pal3.Game.Dev;
using Pal3.Game.GameSystems.Caption;
using Pal3.Game.Scene;
using UnrealSharp.Engine;
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

        private URuntimeJsonWidget? _diagnostics;
        private URuntimeJsonWidget? _dialogue;
        private URuntimeJsonWidget? _loading;
        private URuntimeJsonWidget? _caption;
        private UTexture2D? _captionTexture;
        private string? _loadingText;
        private float _loadingProgress = float.NaN;
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
                UpdateCaption();
            }
            catch (Exception e)
            {
                _failed = true;
                UnrealSharp.Log.UnrealLogger.LogError("Pal3", "[PAL3 HUD] " + e);
            }
        }

        private void UpdateLoading()
        {
            string? phase = null;
            float progress = -1f;
            if (Pal3Boot.IsGameReady)
            {
                var services = ServiceLocator.Instance;
                var scene = services.Get<SceneManager>();
                var video = services.Get<Pal3.Game.Video.VideoManager>();
                if (scene.IsLoading)
                {
                    phase = scene.LoadingPhase;
                    progress = scene.LoadingProgress;
                }
                else if (video.IsPreparing)
                {
                    phase = video.PreparationPhase;
                    progress = video.PreparationProgress;
                }
            }

            if (phase == null)
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
            if (_loadingText != phase)
            {
                _loadingText = phase;
                _loading.SetElementText("loading_phase", _loadingText);
            }
            if (_loadingProgress != progress)
            {
                _loadingProgress = progress;
                _loading.SetElementVisible("loading_percent", progress >= 0);
                _loading.SetElementProgress("loading_progress", Math.Clamp(progress, 0f, 1f));
                if (progress >= 0) _loading.SetElementText("loading_percent", $"{(int)(Math.Clamp(progress, 0f, 1f) * 100)}%");
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

            if (_dialogue == null)
            {
                _dialogue = Pal3UiLayout.Create("Pal3Dialogue", 60);
                if (_dialogue == null) return;
                _dialogue.SetElementVisible("keyboard_continue", !Pal3TouchStick.IsSupported);
            }

            var portrait = DialoguePresentation.Avatar;
            if (_lastDialogue == text && _lastPortrait == portrait) return;
            _lastDialogue = text;
            _lastPortrait = portrait;

            string textId = portrait == null ? "plain" : portrait.RightAligned ? "withRight" : "withLeft";
            _dialogue.SetElementVisible("plainScroll", textId == "plain");
            _dialogue.SetElementVisible("withLeftScroll", textId == "withLeft");
            _dialogue.SetElementVisible("withRightScroll", textId == "withRight");
            _dialogue.SetElementText(textId, text);
            UpdatePortrait("leftPortrait", portrait is { RightAligned: false } ? portrait : null);
            UpdatePortrait("rightPortrait", portrait is { RightAligned: true } ? portrait : null);
            _dialogue.Visibility = ESlateVisibility.SelfHitTestInvisible;
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

        private void UpdateCaption()
        {
            var caption = Pal3Boot.IsGameReady ? ServiceLocator.Instance.Get<CaptionRenderer>() : null;
            var texture = caption?.DisplayTexture;
            if (texture == null)
            {
                if (_caption != null && _captionTexture != null)
                {
                    _caption.SetElementImage("caption", null!);
                    SetVisible(_caption, false);
                    _captionTexture = null;
                }
                return;
            }

            _caption ??= Pal3UiLayout.Create("Pal3Caption", 75);
            if (_caption == null) return;
            if (_captionTexture != texture)
            {
                _caption.SetElementImage("caption", texture);
                _captionTexture = texture;
            }
            _caption.SetElementRenderOpacity("caption", caption!.Opacity);
            SetVisible(_caption, true);
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
            if (_caption != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_caption);
            _diagnostics = null;
            _dialogue = null;
            _loading = null;
            _caption = null;
            _captionTexture = null;
        }
    }
}
