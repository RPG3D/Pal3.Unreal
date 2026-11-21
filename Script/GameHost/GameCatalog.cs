using System.Text.Json;
using GameHost.Contracts;
using UnrealSharp.SharpDemo;
using UnrealSharp.Plugins;

namespace GameHost;

internal sealed class GameCatalog : IGameCatalog
{
    private sealed class CatalogDocument
    {
        public List<GameDescriptor> Games { get; set; } = new();
    }

    private readonly string _selectionPath;
    public IReadOnlyList<GameDescriptor> Games { get; }
    public string SelectedGame { get; private set; }

    public GameCatalog()
    {
        UnrealSharp.Engine.SystemLibrary.ParseCommandLine(UnrealSharp.Engine.SystemLibrary.CommandLine,
            out _, out _, out var parameters);
        string path = Path.Combine(UProjectFileLibrary.ProjectDirectory,
            parameters.TryGetValue("GameHostCatalog", out string? catalogPath) ? catalogPath : "Script/GameHost/Resources/Games.json");
        if (!UProjectFileLibrary.ReadText(path, out string text)) throw new IOException("Cannot read game catalog: " + path);
        Games = JsonSerializer.Deserialize<CatalogDocument>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Games
            ?? throw new InvalidDataException("Empty game catalog.");
        if (Games.Count == 0 || Games.Select(game => game.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Games.Count)
            throw new InvalidDataException("The game catalog must contain unique game IDs.");
        _selectionPath = Path.Combine(UProjectFileLibrary.SavedDirectory, "GameHost/SelectedGame.txt");
        // Read the previous product preference once; future writes use the host namespace.
        if (!UProjectFileLibrary.ReadText(_selectionPath, out string selected))
            UProjectFileLibrary.ReadText(Path.Combine(UProjectFileLibrary.SavedDirectory, "Pal3Variant.txt"), out selected);
        SelectedGame = Games.FirstOrDefault(game => game.Id.Equals(selected.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
            ?? Games[0].Id;
    }

    public bool IsAvailable(string gameId) => Games.Any(game =>
        game.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase) &&
        PluginLoader.FindPlugin(game.EntryAssembly)?.Assembly?.IsAlive == true);

    public void Select(string gameId)
    {
        string id = Games.Single(game => game.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase)).Id;
        if (!UProjectFileLibrary.WriteTextAtomic(_selectionPath, id)) throw new IOException("Cannot save the selected game.");
        SelectedGame = id;
    }

}
