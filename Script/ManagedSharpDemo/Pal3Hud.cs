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
using UnrealSharp.SlateCore;
using UnrealSharp.UMG;

namespace ManagedSharpDemo
{
    internal sealed class Pal3Hud : IDisposable
    {
        // Width of loading_progress in Pal3Loading.json.
        private const float LoadingBarWidth = 1080f;
        private const string LoadingBarFrameTextureName = "loading_1.tga";
        private const string LoadingBarFillTextureName = "loading_2.tga";
        private const string LoadingDecorationTextureName = "loading_4.tga";
        private const double LoadingMarkerFrameSeconds = 0.15;
        private static readonly string[] LoadingMarkerFrameNames = ["loading_3.tga", "loading_5.tga"];

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
        private readonly List<UnityTexture2D> _loadingArtwork = new();
        private UnityTexture2D[] _loadingMarkerFrames = [];
        private FBox2f _loadingMarkerUv;
        private int _displayedLoadingMarkerFrame = -1;
        private double _loadingMarkerTime;
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
                UpdateLoading(deltaTime);
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

        private void UpdateLoading(float deltaTime)
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
                _loadingMarkerTime = 0;
                return;
            }

            if (_loading == null)
            {
                _loading = Pal3UiLayout.Create("Pal3Loading", 120);
                if (_loading == null) return;
                LoadLoadingLogo(_loading);
                LoadLoadingArtwork(_loading);
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
                bool showMarker = progress >= 0 && _loadingMarkerFrames.Length > 0;
                _loading.SetElementVisible("loading_huaying", showMarker);
                if (showMarker)
                    _loading.SetElementRenderTranslation("loading_huaying", new FVector2D(LoadingBarWidth * fraction, 0));
                if (progress >= 0) _loading.SetElementText("loading_percent", $"{(int)(fraction * 100)}%");
            }

            if (progress >= 0) UpdateLoadingMarker(_loading, deltaTime);
            else _loadingMarkerTime = 0;
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
                    var uv = logo.GetTextureUVs();
                    if (widget.SetElementImage("loading_logo", nativeTexture, uv.Left, uv.Top, uv.Right, uv.Bottom))
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

        private void LoadLoadingArtwork(URuntimeJsonWidget widget)
        {
            // The HUD owns the textures across scene cache clears.
            string folder = $"{FileConstants.UIFolderVirtualPath}Loading{CpkConstants.DirectorySeparatorChar}";
            var provider = ServiceLocator.Instance.Get<GameResourceProvider>()
                .CreateTextureResourceProvider(folder, useCache: false);

            bool hasFrame = LoadImage("loading_bar_frame", LoadingBarFrameTextureName);
            LoadImage("loading_decoration", LoadingDecorationTextureName);
            var fill = LoadLoadingTexture(provider, LoadingBarFillTextureName);
            if (widget.WidgetTree.RootWidget is UCanvasPanel root &&
                root.GetChildAt(0) is UBorder { Content: UCanvasPanel panel } &&
                Enumerable.Range(0, panel.ChildrenCount).Select(panel.GetChildAt).OfType<UProgressBar>().FirstOrDefault() is { } bar)
            {
                var style = bar.Style;
                if (hasFrame) style.BackgroundImage.DrawAs = ESlateBrushDrawType.NoDrawType;
                if (fill is { UeTexture: { } nativeTexture })
                {
                    style.FillImage.DrawAs = ESlateBrushDrawType.Image;
                    style.FillImage.Tiling = ESlateBrushTileType.NoTile;
                    style.FillImage.Image = nativeTexture;
                    style.FillImage.UVRegion = AlphaBounds(fill);
                    style.FillImage.Tint.SpecifiedColor = FLinearColor.White;
                    style.EnableFillAnimation = false;
                    bar.FillColorAndOpacity = FLinearColor.White;
                    bar.BarFillStyle = UnrealSharp.Slate.EProgressBarFillStyle.Mask;
                }
                bar.Style = style;
            }

            // Use the same crop for both frames to keep their original alignment.
            var frames = new List<UnityTexture2D>(LoadingMarkerFrameNames.Length);
            var min = new FVector2f(1, 1);
            var max = new FVector2f(0, 0);
            foreach (string name in LoadingMarkerFrameNames)
            {
                var frame = LoadLoadingTexture(provider, name);
                if (frame == null) continue;

                var uv = AlphaBounds(frame);
                min = new FVector2f(Math.Min(min.X, uv.Min.X), Math.Min(min.Y, uv.Min.Y));
                max = new FVector2f(Math.Max(max.X, uv.Max.X), Math.Max(max.Y, uv.Max.Y));
                frames.Add(frame);
            }
            _loadingMarkerFrames = frames.ToArray();
            _loadingMarkerUv = new FBox2f(min, max, frames.Count > 0);

            bool LoadImage(string id, string name)
            {
                var texture = LoadLoadingTexture(provider, name);
                bool bound = texture is { UeTexture: { } native } && SetLoadingImage(widget, id, native, AlphaBounds(texture));
                widget.SetElementVisible(id, bound);
                return bound;
            }
        }

        private UnityTexture2D? LoadLoadingTexture(ITextureResourceProvider provider, string name)
        {
            ITexture2D? texture = null;
            try
            {
                texture = provider.GetTexture(name);
                if (texture is UnityTexture2D { UeTexture: not null } uploaded)
                {
                    _loadingArtwork.Add(uploaded);
                    return uploaded;
                }
            }
            catch (Exception error)
            {
                UnrealSharp.Log.UnrealLogger.LogWarning("Pal3", $"Could not load PAL3 loading texture '{name}': {error.Message}");
            }
            texture?.Destroy();
            return null;
        }

        private static FBox2f AlphaBounds(UnityTexture2D texture)
        {
            texture.TryGetAlphaBounds(out float u0, out float v0, out float u1, out float v1);
            return new FBox2f(new FVector2f(u0, v0), new FVector2f(u1, v1), true);
        }

        private static bool SetLoadingImage(URuntimeJsonWidget widget, string id, UTexture2D texture, FBox2f uv) =>
            widget.SetElementImage(id, texture, uv.Min.X, uv.Min.Y, uv.Max.X, uv.Max.Y);

        private void UpdateLoadingMarker(URuntimeJsonWidget widget, float deltaTime)
        {
            if (_loadingMarkerFrames.Length == 0) return;
            int frame = (int)(_loadingMarkerTime / LoadingMarkerFrameSeconds);
            if (frame != _displayedLoadingMarkerFrame && _loadingMarkerFrames[frame].UeTexture is { } native &&
                SetLoadingImage(widget, "loading_huaying", native, _loadingMarkerUv))
            {
                _displayedLoadingMarkerFrame = frame;
            }
            if (_loadingMarkerFrames.Length > 1)
                _loadingMarkerTime = (_loadingMarkerTime + deltaTime) % (LoadingMarkerFrameSeconds * _loadingMarkerFrames.Length);
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
            foreach (var texture in _loadingArtwork) texture.Destroy();
            _loadingArtwork.Clear();
            _diagnostics = null;
            _dialogue = null;
            _loading = null;
            _caption = null;
            _cameraCurtain = null;
            _loadingLogoTexture = null;
            _loadingMarkerFrames = [];
            _displayedLoadingMarkerFrame = -1;
            _loadingMarkerTime = 0;
            _captionTexture = null;
        }
    }
}
