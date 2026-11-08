using Pal3.Game;
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
    private const string BaseId = "stick_base";
    private const string ThumbId = "stick_thumb";
    private const float BaseDiameter = 160f; // Slate units in Pal3TouchStick.json
    private readonly OnScreenStick _stick = new() { movementRange = 90f }; // PAL3.Unity prefab
    private URuntimeJsonWidget? _widget;
    private bool _visible;
    private int _pressedPointers;
    private Vector2 _thumb;

    public static bool IsSupported { get; } = UGameplayStatics.PlatformName is "Android" or "IOS" ||
            Environment.GetEnvironmentVariable("PAL3_TOUCH_STICK") == "1";

    public Vector2 Tick(APlayerController pc, bool menuHasInput)
    {
        if (!IsSupported) return Vector2.zero;
        bool enabled = !menuHasInput && Pal3Boot.HasPlayerControl();
        if (enabled && _widget == null)
        {
            _widget = Pal3UiLayout.Create("Pal3TouchStick", 60);
            _visible = _widget != null;
        }
        if (_widget != null && _visible != enabled)
        {
            _widget.Visibility = enabled ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed;
            _visible = enabled;
        }

        // Read viewport pixels from UE and map them back to the layout's UI units.
        // Remember held fingers even while hidden so a menu touch cannot start a drag.
        FVector2D origin = default;
        FVector2D size = default;
        bool canDrag = enabled && _widget != null && _widget.TryGetElementViewportRect(BaseId, out origin, out size);
        if (!canDrag) _stick.CancelInteraction();

        int pressedPointers = 0;
        for (int finger = (int)ETouchIndex.Touch1; finger <= (int)ETouchIndex.Touch10; finger++)
        {
            pc.GetInputTouchState((ETouchIndex)finger, out float x, out float y, out bool pressed);
            if (!pressed)
            {
                _stick.EndInteraction(finger);
                continue;
            }

            int mask = 1 << finger;
            pressedPointers |= mask;
            if (!canDrag) continue;

            var point = new Vector2(
                (float)((x - origin.X) / size.X - 0.5) * BaseDiameter,
                (float)(0.5 - (y - origin.Y) / size.Y) * BaseDiameter);
            if ((_pressedPointers & mask) == 0 && point.sqrMagnitude <= BaseDiameter * BaseDiameter / 4)
            {
                _stick.BeginInteraction(finger, point);
            }
            _stick.MoveStick(finger, point);
        }
        _pressedPointers = pressedPointers;

        Vector2 displacement = _stick.Displacement;
        if (_widget != null && displacement != _thumb)
        {
            _widget.SetElementRenderTranslation(ThumbId, new FVector2D(displacement.x, -displacement.y));
            _thumb = displacement;
        }
        return _stick.value;
    }

    public void Dispose()
    {
        _stick.CancelInteraction();
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }
}
