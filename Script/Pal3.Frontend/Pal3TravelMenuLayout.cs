using System.Text.Json;

namespace ManagedSharpDemo;

internal static class Pal3TravelMenuLayout
{
    public static string Build(string title, IReadOnlyList<(string Id, string Label)> destinations,
        string backId, string backLabel, string emptyText, object keyActions)
    {
        const int columns = 4;
        const int buttonWidth = 210;
        const int gap = 8;
        var rows = destinations.Select((destination, index) => Button(destination.Id, destination.Label,
            (index % columns) * (buttonWidth + gap), (index / columns) * (58 + gap), buttonWidth)).ToArray();

        return JsonSerializer.Serialize(new
        {
            keyActions,
            root = new
            {
                type = "canvas",
                children = new[]
                {
                    new
                    {
                        type = "border", anchor = "center", size = new[] { 936, 652 },
                        color = new[] { 0.035, 0.055, 0.075, 0.98 },
                        child = new
                        {
                            type = "canvas",
                            children = new object[]
                            {
                                Text("title", title, 28, 20, 880, 52, 36),
                                new
                                {
                                    type = "scrollbox", position = new[] { 28, 86 }, size = new[] { 880, 452 },
                                    child = new { type = "canvas", children = rows }
                                },
                                Text("menu_status", destinations.Count == 0 ? emptyText : "", 28, 576, 700, 48, 24),
                                Button(backId, backLabel, 754, 566, 154)
                            }
                        }
                    }
                }
            }
        });
    }

    private static object Text(string id, string text, int x, int y, int width, int height, int fontSize) => new
    {
        type = "text", id, text, fontSize, align = "left", color = new[] { 0.96, 0.89, 0.74, 1.0 },
        position = new[] { x, y }, size = new[] { width, height }
    };

    private static object Button(string id, string text, int x, int y, int width) => new
    {
        type = "button", id, text, fontSize = 24, wrap = true, normalColor = new[] { 0.11, 0.22, 0.29, 1.0 },
        position = new[] { x, y }, size = new[] { width, 58 }
    };
}
