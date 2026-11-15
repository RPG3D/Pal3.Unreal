using Engine.Diagnostics;
using Engine.Core.Abstraction;
using Engine.Services;
using Pal3.Core.Contract.Constants;
using Pal3.Core.DataReader.Cpk;
using Pal3.Game;
using Pal3.Game.Camera;
using Pal3.Game.Data;
using Pal3.Game.Dev;
using Pal3.Game.GameSystems.Caption;
using Pal3.Game.Scene;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo
{
    internal sealed class Pal3Hud : IDisposable
    {
        // Width of loading_progress in Pal3Loading.json.
        private const float LoadingBarWidth = 1120f;
        private const string LoadingMarkerTextureName = "loading_3.tga";

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

        private const string CameraCurtainLayout = """
        {
          "root": { "type": "canvas", "children": [
            { "type": "border", "id": "black", "anchor": "full", "color": [0, 0, 0, 1] },
            { "type": "border", "id": "white", "anchor": "full", "color": [1, 1, 1, 1] }
          ]}
        }
        """;

        private URuntimeJsonWidget? _diagnostics;
        private URuntimeJsonWidget? _dialogue;
        private URuntimeJsonWidget? _loading;
        private URuntimeJsonWidget? _caption;
        private URuntimeJsonWidget? _cameraCurtain;
        private UTexture2D? _captionTexture;
        private ITexture2D? _loadingLogoTexture;
        private ITexture2D? _loadingMarkerTexture;
        private string? _loadingText;
        private float _loadingProgress = float.NaN;
        private int _logVersion = -1;
        private float _diagnosticsTimer;
        private string _lastDialogue = "";
        private DialoguePresentation.Portrait? _lastPortrait;
        private bool _lastDialogueWasChoice;
        private bool _failed;

        public void Tick(float deltaTime)
        {
            if (_failed) return;
            try
            {
                UpdateCameraCurtain();
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

        private void UpdateCameraCurtain()
        {
            UnityEngine.Color color = Pal3Boot.IsGameReady && !Pal3Boot.WaitingForMenuStart
                ? ServiceLocator.Instance.Get<CameraManager>().CurtainColor
                : UnityEngine.Color.clear;
            if (color.a <= 0f)
            {
                if (_cameraCurtain != null) SetVisible(_cameraCurtain, false);
                return;
            }

            // Cover the scene beneath the loading, video and dialogue layers.
            _cameraCurtain ??= URuntimeJsonWidgetLibrary.CreatePassiveJsonWidget(CameraCurtainLayout, 40);
            if (_cameraCurtain == null) return;
            bool white = color.r > 0.5f;
            _cameraCurtain.SetElementRenderOpacity("black", white ? 0f : color.a);
            _cameraCurtain.SetElementRenderOpacity("white", white ? color.a : 0f);
            SetVisible(_cameraCurtain, true);
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
                LoadLoadingLogo(_loading);
                LoadProgressMarker(_loading);
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
                float fraction = Math.Clamp(progress, 0f, 1f);
                _loading.SetElementVisible("loading_percent", progress >= 0);
                _loading.SetElementProgress("loading_progress", fraction);
                bool showMarker = progress >= 0 && _loadingMarkerTexture != null;
                _loading.SetElementVisible("loading_huaying", showMarker);
                if (showMarker)
                    _loading.SetElementRenderTranslation("loading_huaying", new FVector2D(LoadingBarWidth * fraction, 0));
                if (progress >= 0) _loading.SetElementText("loading_percent", $"{(int)(fraction * 100)}%");
            }
        }

        private void LoadLoadingLogo(URuntimeJsonWidget widget)
        {
            widget.SetElementVisible("loading_logo", false);
            ISprite? sprite = null;
            try
            {
                // Keep the loading logo alive while the scene texture cache is cleared.
                sprite = ServiceLocator.Instance.Get<GameResourceProvider>().GetLogoSprite(useCache: false);
                if (sprite is UnitySprite { Texture: UnityTexture2D { UeTexture: { } nativeTexture } texture } logo)
                {
                    var rect = logo.TextureRect;
                    // The Unity sprite rectangle is bottom-up, while UMG UVs are top-down.
                    float u0 = rect.x / texture.Width;
                    float v0 = 1f - (rect.y + rect.height) / texture.Height;
                    float u1 = (rect.x + rect.width) / texture.Width;
                    float v1 = 1f - rect.y / texture.Height;
                    if (widget.SetElementImage("loading_logo", nativeTexture, u0, v0, u1, v1))
                    {
                        _loadingLogoTexture = texture;
                        widget.SetElementVisible("loading_title", false);
                        widget.SetElementVisible("loading_logo", true);
                    }
                }
            }
            catch (Exception error)
            {
                UnrealSharp.Log.UnrealLogger.LogWarning("Pal3", $"Could not load the PAL3 loading logo: {error.Message}");
            }
            finally
            {
                if (_loadingLogoTexture == null) sprite?.Texture.Destroy();
                sprite?.Destroy();
            }
        }

        private void LoadProgressMarker(URuntimeJsonWidget widget)
        {
            ITexture2D? texture = null;
            try
            {
                // Own this texture in the HUD since scene loads clear the shared texture cache.
                string folder = $"{FileConstants.UIFolderVirtualPath}Loading{CpkConstants.DirectorySeparatorChar}";
                texture = ServiceLocator.Instance.Get<GameResourceProvider>()
                    .CreateTextureResourceProvider(folder, useCache: false).GetTexture(LoadingMarkerTextureName);
                if (texture is UnityTexture2D { UeTexture: { } nativeTexture } marker)
                {
                    marker.TryGetAlphaBounds(out float u0, out float v0, out float u1, out float v1);
                    if (widget.SetElementImage("loading_huaying", nativeTexture, u0, v0, u1, v1))
                    {
                        _loadingMarkerTexture = marker;
                        return;
                    }
                }
            }
            catch (Exception error)
            {
                UnrealSharp.Log.UnrealLogger.LogWarning("Pal3", $"Could not load the PAL3 progress marker: {error.Message}");
            }
            texture?.Destroy();
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
            bool choosing = DialoguePresentation.Choices.Count > 0;
            string text = choosing ? DialoguePresentation.ChoicePrompt : DialoguePresentation.Text;
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
            }

            var portrait = choosing ? DialoguePresentation.ChoiceAvatar : DialoguePresentation.Avatar;
            if (_lastDialogue == text && _lastPortrait == portrait && _lastDialogueWasChoice == choosing) return;
            _lastDialogue = text;
            _lastPortrait = portrait;
            _lastDialogueWasChoice = choosing;

            string textId = portrait == null ? "plain" : portrait.RightAligned ? "withRight" : "withLeft";
            _dialogue.SetElementVisible("keyboard_continue", !choosing && !Pal3TouchStick.IsSupported);
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
            if (_cameraCurtain != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_cameraCurtain);
            _loadingLogoTexture?.Destroy();
            _loadingMarkerTexture?.Destroy();
            _diagnostics = null;
            _dialogue = null;
            _loading = null;
            _caption = null;
            _cameraCurtain = null;
            _loadingLogoTexture = null;
            _loadingMarkerTexture = null;
            _captionTexture = null;
        }
    }
}
