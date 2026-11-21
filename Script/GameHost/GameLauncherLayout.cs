using System.Text.Json.Nodes;
using GameHost.Contracts;

namespace GameHost;

internal static class GameLauncherLayout
{
    public const string GameButtonPrefix = "host_game:";

    // The JSON owns geometry and styling; C# only repeats its row template for
    // catalog data. Every game shares one continuous list, without page state.
    public static string BindGames(string json, IReadOnlyList<GameDescriptor> games)
    {
        JsonNode document = JsonNode.Parse(json) ?? throw new InvalidDataException("Empty game-list layout.");
        JsonNode template = document["gameItemTemplate"]
            ?? throw new InvalidDataException("Game-list layout has no item template.");
        double x = template["position"]![0]!.GetValue<double>();
        double y = template["position"]![1]!.GetValue<double>();
        double step = template["size"]![1]!.GetValue<double>() + document["gameItemGap"]!.GetValue<double>();
        JsonNode list = Find(document["root"]!, "host_game_rows")
            ?? throw new InvalidDataException("Game-list layout has no row container.");
        var rows = new JsonArray();
        for (int index = 0; index < games.Count; index++)
        {
            JsonNode row = template.DeepClone();
            row["id"] = GameButtonPrefix + games[index].Id;
            row["text"] = games[index].Title;
            row["textKey"] = "host.game." + games[index].Id;
            row["position"] = new JsonArray(x, y + index * step);
            rows.Add(row);
        }
        list["children"] = rows;
        return document.ToJsonString();
    }

    private static JsonNode? Find(JsonNode node, string id)
    {
        if (node["id"]?.GetValue<string>() == id) return node;
        if (node["child"] is { } child && Find(child, id) is { } found) return found;
        if (node["children"] is JsonArray children)
            foreach (JsonNode? item in children)
                if (item != null && Find(item, id) is { } match) return match;
        return null;
    }
}
