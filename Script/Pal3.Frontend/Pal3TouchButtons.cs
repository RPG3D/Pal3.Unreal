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
    private (bool Menu, bool Interact, bool Continue, bool Skip) _visible;

    public const string Menu = "touch_menu";
    public const string Interact = "touch_interact";
    public const string Continue = "touch_continue";
    private const string ActionButton = "touch_action";

    public string ConsumeClick()
    {
        if (_widget == null) return "";
        string id = Pal3UiLayout.ConsumeClick(_widget);
        return id switch
        {
            Menu when _visible.Menu => id,
            ActionButton when _visible.Interact => Interact,
            ActionButton when _visible.Continue || _visible.Skip => Continue,
            _ => ""
        };
    }

    public void Tick(bool inputBlocked)
    {
        if (!Pal3TouchStick.IsSupported) return;

        var services = ServiceLocator.Instance;
        GameState? state = !inputBlocked && Pal3Boot.IsGameReady &&
            !services.Get<SceneManager>().IsLoading
            ? services.Get<GameStateManager>().GetCurrentState() : null;
        if (Pal3Boot.WaitingForMenuStart && state != GameState.VideoPlaying) state = null;
        var visible = (Menu: state is GameState.Gameplay or GameState.Cutscene or GameState.Combat,
            Interact: state == GameState.Gameplay && Pal3Boot.HasPlayerControl(),
            Continue: state == GameState.Cutscene,
            Skip: state == GameState.VideoPlaying);
        if (_visible == visible) return;

        if (_widget == null)
        {
            // Keep Skip above the shared loading screen while a video is being prepared.
            _widget = Pal3UiLayout.Create("Pal3TouchButtons", 130);
            if (_widget == null) return;
        }

        _visible = visible;
        _widget.SetElementVisible(Menu, visible.Menu);
        _widget.SetElementVisible(ActionButton, visible.Interact || visible.Continue || visible.Skip);
        _widget.SetElementText(ActionButton, visible.Skip ? "跳过" : visible.Continue ? "继续" : "交互");
        _widget.Visibility = visible.Menu || visible.Skip ? ESlateVisibility.SelfHitTestInvisible : ESlateVisibility.Collapsed;
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }
}
