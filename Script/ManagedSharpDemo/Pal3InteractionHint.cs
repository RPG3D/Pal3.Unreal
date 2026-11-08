using Engine.Core.Implementation;
using Engine.Services;
using Pal3.Game;
using Pal3.Game.GamePlay;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal sealed class Pal3InteractionHint : IDisposable
{
    private URuntimeJsonWidget? _widget;
    private PlayerGamePlayManager.InteractionTarget? _target;
    private float _untilRefresh;
    private bool _layoutFailed;

    public void Tick(APlayerController pc, float deltaTime, bool inputBlocked)
    {
        if (inputBlocked || !Pal3Boot.HasPlayerControl())
        {
            _target = null;
            _untilRefresh = 0;
            SetVisible(false);
            return;
        }

        _untilRefresh -= deltaTime;
        if (_untilRefresh <= 0)
        {
            _untilRefresh = .1f;
            _target = ServiceLocator.Instance.Get<PlayerGamePlayManager>().GetInteractionTarget();
        }

        if (_layoutFailed || _target is not { } target ||
            !UWidgetLayoutLibrary.ProjectWorldLocationToWidgetPosition(pc,
                UnityUeConvert.ToUePosition(target.HintPosition), out var position, false))
        {
            SetVisible(false);
            return;
        }

        if (_widget == null)
        {
            _widget = Pal3UiLayout.Create("Pal3InteractionHint", 55);
            if (_widget == null)
            {
                _layoutFailed = true;
                return;
            }
            if (Pal3TouchStick.IsSupported) _widget.SetElementText("prompt", "交互");
        }
        _widget.SetElementRenderTranslation("target", position);
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        var visibility = visible ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed;
        if (_widget != null && _widget.Visibility != visibility) _widget.Visibility = visibility;
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }
}
