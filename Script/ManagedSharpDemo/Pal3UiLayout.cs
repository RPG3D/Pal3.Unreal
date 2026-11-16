using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.SlateCore;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal static class Pal3UiLayout
{
    public static string ConsumeClick(URuntimeJsonWidget widget) =>
        widget.ConsumeClickedButton(out var id) ? id.ToString().ToLowerInvariant() : string.Empty;

    public static void ConfigureMenuScroll(URuntimeJsonWidget widget)
    {
        if (widget.WidgetTree.RootWidget is not UCanvasPanel root ||
            root.GetChildAt(0) is not UBorder { Content: UCanvasPanel panel } ||
            panel.GetChildAt(1) is not UScrollBox scroll ||
            scroll.GetChildAt(0) is not UCanvasPanel rows)
            return;

        scroll.AlwaysShowScrollbar = true;
        for (int i = 0; i < rows.ChildrenCount; i++)
        {
            // Let the scroll box capture a swipe that starts on a button.
            if (rows.GetChildAt(i) is UButton row) row.TouchMethod = EButtonTouchMethod.PreciseTap;
        }
    }

    public static URuntimeJsonWidget Create(string name, int zOrder, bool interactive = false, string? overridePath = null)
    {
        string path = string.IsNullOrWhiteSpace(overridePath)
            ? $"Script/ManagedSharpDemo/Layouts/{name}.json"
            : overridePath;
        return URuntimeJsonWidgetLibrary.CreateJsonWidgetFromFile(path, zOrder, interactive);
    }
}
