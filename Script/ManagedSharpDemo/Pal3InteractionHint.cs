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
    private UnityEngine.Vector3? _crossing;
    private float _untilRefresh;
    private bool _layoutFailed;

    public void Tick(APlayerController pc, float deltaTime, bool inputBlocked)
    {
        if (inputBlocked || !Pal3Boot.HasPlayerControl())
        {
            _target = null;
            _crossing = null;
            _untilRefresh = 0;
            SetVisible(false);
            return;
        }

        _untilRefresh -= deltaTime;
        if (_untilRefresh <= 0)
        {
            _untilRefresh = .1f;
            var gameplay = ServiceLocator.Instance.Get<PlayerGamePlayManager>();
            _crossing = gameplay.GetRiverCrossingHint();
            _target = _crossing.HasValue ? null : gameplay.GetInteractionTarget();
        }

        if (_layoutFailed || (_crossing ?? _target?.HintPosition) is not { } hintPosition ||
            !UWidgetLayoutLibrary.ProjectWorldLocationToWidgetPosition(pc,
                UnityUeConvert.ToUePosition(hintPosition), out var position, false))
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
        }
        bool touch = Pal3TouchStick.IsSupported;
        string prompt = _crossing.HasValue ? (touch ? "过河" : "E  过河") : (touch ? "交互" : "E  交互");
        if (_prompt != prompt)
        {
            _widget.SetElementText("prompt", prompt);
            _prompt = prompt;
        }
        _widget.SetElementRenderTranslation("target", position);
        SetVisible(true);
    }

    private string? _prompt;

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
