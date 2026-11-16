using Engine.Services;
using Pal3.Core.Contract.Constants;
using Pal3.Game;
using Pal3.Game.GameSystems.WorldMap;
using UnrealSharp.Engine;
using UnrealSharp.InputCore;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal sealed class Pal3WorldMapMenu : IDisposable
{
    private URuntimeJsonWidget? _widget;

    public bool Tick(APlayerController pc)
    {
        var map = Pal3Boot.IsGameReady ? ServiceLocator.Instance.Get<WorldMapManager>() : null;
        if (map?.IsVisible() != true)
        {
            if (_widget == null) return false;
            CloseView(pc);
            return true;
        }

        if (_widget == null && !Show(map))
        {
            map.Close();
            CloseView(pc);
            return true;
        }

        string id = Pal3UiLayout.ConsumeClick(_widget!);
        if (id == "close" || pc.WasInputKeyJustPressed(Keys.Escape) || pc.WasInputKeyJustPressed(Keys.M) ||
            pc.WasInputKeyJustPressed(Keys.Gamepad_Special_Right) || pc.WasInputKeyJustPressed(Keys.Android_Back))
        {
            map.Close();
            CloseView(pc);
        }
        else if (id.StartsWith("region_", StringComparison.Ordinal) && int.TryParse(id.AsSpan(7), out int region))
        {
            if (map.TryTravel(region)) CloseView(pc);
            else _widget!.SetElementText("menu_status", "无法前往该地点");
        }

        return true;
    }

    private bool Show(WorldMapManager map)
    {
        try
        {
            _widget = URuntimeJsonWidgetLibrary.CreateJsonWidget(BuildLayout(map), 115);
            if (_widget == null) return false;
            Pal3UiLayout.ConfigureMenuScroll(_widget);
            return true;
        }
        catch (Exception error)
        {
            Engine.Diagnostics.DebugOverlay.Error("[PAL3 world map] " + error);
            return false;
        }
    }

    private static string BuildLayout(WorldMapManager map)
    {
        var destinations = new List<(string Id, string Label)>();
        string[] names = WorldMapConstants.WorldMapRegions;
        for (int region = 0; region < names.Length; region++)
        {
            if (map.CanTravelTo(region)) destinations.Add(($"region_{region}", names[region]));
        }

        return Pal3TravelMenuLayout.Build("大地图", destinations, "close", "关闭", "暂无可前往的地点",
            new { Escape = "close", M = "close", Gamepad_Special_Right = "close", Android_Back = "close" });
    }

    private void CloseView(APlayerController pc)
    {
        Dispose();
        pc.ShowMouseCursor = false;
        WidgetLibrary.SetInputMode_GameOnly(pc, flushInput: true);
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
    }
}
