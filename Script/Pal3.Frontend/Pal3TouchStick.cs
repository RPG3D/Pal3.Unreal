using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.InputCore;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal sealed class Pal3TouchStick : IDisposable
{
    private const float BaseDiameter = 320f; // Slate units in Pal3TouchStick.json
    private readonly Stick _movement = new("stick_base", "stick_thumb");
    private readonly Stick _camera = new("camera_base", "camera_thumb");
    private URuntimeJsonWidget? _widget;
    private bool _visible;
    private bool _cameraVisible;
    private bool _fullyTransparent;
    private int _pressedPointers;

    public static bool IsSupported { get; } = UGameplayStatics.PlatformName is "Android" or "IOS" ||
            Environment.GetEnvironmentVariable("PAL3_TOUCH_STICK") == "1";

    public bool FullyTransparent
    {
        get => _fullyTransparent;
        set
        {
            _fullyTransparent = value;
            if (_widget != null) _widget.RenderOpacity = value ? 0f : 1f;
        }
    }

    public (Vector2 Movement, float CameraX) Tick(APlayerController pc, bool enabled, bool cameraEnabled)
    {
        if (!IsSupported) return default;
        if (enabled && _widget == null)
        {
            _widget = Pal3UiLayout.Create("Pal3TouchStick", 60);
            if (_widget != null)
            {
                _widget.RenderOpacity = _fullyTransparent ? 0f : 1f;
                SetCameraVisible(cameraEnabled);
            }
        }
        if (_widget != null && _visible != enabled)
        {
            _widget.Visibility = enabled ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed;
            _visible = enabled;
        }

        if (_widget != null && _cameraVisible != cameraEnabled) SetCameraVisible(cameraEnabled);
        _movement.ReadGeometry(_widget, enabled);
        _camera.ReadGeometry(_widget, cameraEnabled);

        // Remember held fingers even while hidden so a menu touch cannot start a drag.
        int pressedPointers = 0;
        for (int finger = (int)ETouchIndex.Touch1; finger <= (int)ETouchIndex.Touch10; finger++)
        {
            pc.GetInputTouchState((ETouchIndex)finger, out float x, out float y, out bool pressed);
            if (!pressed)
            {
                _movement.EndInteraction(finger);
                _camera.EndInteraction(finger);
                continue;
            }

            int mask = 1 << finger;
            pressedPointers |= mask;
            bool justPressed = (_pressedPointers & mask) == 0;
            if (!_movement.Poll(finger, x, y, justPressed)) _camera.Poll(finger, x, y, justPressed);
        }
        _pressedPointers = pressedPointers;

        _movement.UpdateThumb(_widget);
        _camera.UpdateThumb(_widget);
        return (_movement.Value, _camera.Value.x);
    }

    private void SetCameraVisible(bool visible)
    {
        _widget!.SetElementVisible("camera_base", visible);
        _widget.SetElementVisible("camera_label", visible);
        _cameraVisible = visible;
    }

    public void Dispose()
    {
        _movement.CancelInteraction();
        _camera.CancelInteraction();
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }

    private sealed class Stick(string baseId, string thumbId)
    {
        private readonly OnScreenStick _stick = new() { movementRange = BaseDiameter * (90f / 160f) };
        private FVector2D _origin;
        private FVector2D _size;
        private Vector2 _thumb;
        private bool _canDrag;

        public Vector2 Value => _stick.value;

        public void ReadGeometry(URuntimeJsonWidget? widget, bool enabled)
        {
            _canDrag = enabled && widget != null && widget.TryGetElementViewportRect(baseId, out _origin, out _size);
            if (!_canDrag) _stick.CancelInteraction();
        }

        public bool Poll(int finger, float x, float y, bool justPressed)
        {
            if (!_canDrag) return false;

            // Map viewport pixels to the stick's UI units, with Y pointing up.
            var point = new Vector2(
                (float)((x - _origin.X) / _size.X - 0.5) * BaseDiameter,
                (float)(0.5 - (y - _origin.Y) / _size.Y) * BaseDiameter);
            bool startsHere = justPressed && point.sqrMagnitude <= BaseDiameter * BaseDiameter / 4;
            if (startsHere) _stick.BeginInteraction(finger, point);
            _stick.MoveStick(finger, point);
            return startsHere;
        }

        public void EndInteraction(int finger) => _stick.EndInteraction(finger);
        public void CancelInteraction() => _stick.CancelInteraction();

        public void UpdateThumb(URuntimeJsonWidget? widget)
        {
            Vector2 displacement = _stick.Displacement;
            if (widget == null || displacement == _thumb) return;

            widget.SetElementRenderTranslation(thumbId, new FVector2D(displacement.x, -displacement.y));
            _thumb = displacement;
        }
    }
}
