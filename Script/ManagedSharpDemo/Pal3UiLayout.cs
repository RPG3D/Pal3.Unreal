using UnrealSharp.RuntimeWidgetLayout;

namespace ManagedSharpDemo;

internal static class Pal3UiLayout
{
    public static string ConsumeClick(URuntimeJsonWidget widget) =>
        widget.ConsumeClickedButton(out var id) ? id.ToString().ToLowerInvariant() : string.Empty;

    public static URuntimeJsonWidget Create(string name, int zOrder, bool interactive = false, string? overridePath = null)
    {
        string path = string.IsNullOrWhiteSpace(overridePath)
            ? $"Script/ManagedSharpDemo/Layouts/{name}.json"
            : overridePath;
        return URuntimeJsonWidgetLibrary.CreateJsonWidgetFromFile(path, zOrder, interactive);
    }
}
