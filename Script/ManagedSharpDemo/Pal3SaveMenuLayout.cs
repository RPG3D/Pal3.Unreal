using System.Text.Json;
using Pal3.Game.State;

namespace ManagedSharpDemo;

internal static class Pal3SaveMenuLayout
{
    private const int RowHeight = 56;
    private const int RowSpacing = 64;
    private const int SlotWidth = 462;
    private const int ActionX = 533;
    private const int ActionWidth = 307;
    private static readonly double[] LabelColor = [0.96, 0.89, 0.74, 1];
    private static readonly double[] ButtonColor = [0.11, 0.22, 0.29, 1];

    public static readonly string Json = Build();

    private static string Build()
    {
        var slots = Enumerable.Range(0, SaveManager.ManualSlotCount + 1)
            .Select(slot => Button($"slot_{slot}", 0, slot * RowSpacing, SlotWidth))
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            keyActions = new { Tab = "menu_tab", Escape = "menu_escape" },
            root = new
            {
                type = "canvas",
                children = new[]
                {
                    new
                    {
                        type = "border", anchor = "center", size = new[] { 868, 652 },
                        color = new[] { 0.035, 0.055, 0.075, 0.98 },
                        child = new
                        {
                            type = "canvas",
                            children = new object[]
                            {
                                Text("saves_title", "存档与读档", 28, 20, 812, 54, 36),
                                new
                                {
                                    type = "scrollbox", id = "save_slots", position = new[] { 28, 88 },
                                    size = new[] { 478, 480 },
                                    child = new { type = "canvas", children = slots }
                                },
                                Text("selected_slot_title", "", ActionX, 104, ActionWidth, 50, 30),
                                Text("selected_slot_status", "", ActionX, 158, ActionWidth, 68, 22),
                                Button("load_selected", ActionX, 242, ActionWidth, "读取存档"),
                                Button("save_selected", ActionX, 314, ActionWidth, "写入存档"),
                                Text("save_hint", "进入游戏后可存档", ActionX, 314, ActionWidth, 56, 22),
                                Text("menu_status", "", 28, 583, 626, 48, 22),
                                Button("saves_back", 681, 572, 159, "返回")
                            }
                        }
                    }
                }
            }
        });
    }

    private static object Button(string id, int x, int y, int width, string text = "") => new
    {
        type = "button", id, text, fontSize = 24, position = new[] { x, y },
        size = new[] { width, RowHeight }, normalColor = ButtonColor
    };

    private static object Text(string id, string text, int x, int y, int width, int height, int fontSize) => new
    {
        type = "text", id, text, fontSize, align = "left", wrap = true, color = LabelColor,
        position = new[] { x, y }, size = new[] { width, height }
    };
}
