using Engine.Services;
using Pal3.Game;
using Pal3.Game.Scene;
using Pal3.Game.State;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal sealed class Pal3TouchButtons : IDisposable
{
    private URuntimeJsonWidget? _widget;
    private (bool Menu, bool Interact, bool Continue) _visible;

    public const string Menu = "touch_menu";
    public const string Interact = "touch_interact";
    public const string Continue = "touch_continue";

    public string ConsumeClick()
    {
        if (_widget == null) return "";
        string id = Pal3UiLayout.ConsumeClick(_widget);
        return id switch
        {
            Menu when _visible.Menu => id,
            Interact when _visible.Interact => id,
            Continue when _visible.Continue => id,
            _ => ""
        };
    }

    public void Tick(bool inputBlocked)
    {
        if (!Pal3TouchStick.IsSupported) return;

        var services = ServiceLocator.Instance;
        GameState? state = !inputBlocked && Pal3Boot.IsGameReady && !Pal3Boot.WaitingForMenuStart &&
            !services.Get<SceneManager>().IsLoading
            ? services.Get<GameStateManager>().GetCurrentState() : null;
        var visible = (Menu: state is GameState.Gameplay or GameState.Cutscene or GameState.Combat,
            Interact: state == GameState.Gameplay && Pal3Boot.HasPlayerControl(),
            Continue: state == GameState.Cutscene);
        if (_visible == visible) return;

        if (_widget == null)
        {
            _widget = Pal3UiLayout.Create("Pal3TouchButtons", 80);
            if (_widget == null) return;
        }

        _visible = visible;
        _widget.SetElementVisible(Menu, visible.Menu);
        _widget.SetElementVisible(Interact, visible.Interact);
        _widget.SetElementVisible(Continue, visible.Continue);
        _widget.Visibility = visible.Menu ? ESlateVisibility.SelfHitTestInvisible : ESlateVisibility.Collapsed;
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }
}
