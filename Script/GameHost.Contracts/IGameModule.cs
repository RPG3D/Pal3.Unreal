namespace GameHost.Contracts;

/// <summary>UE-facing adapter. Domain code remains owned by the game project.</summary>
public interface IGameModule
{
    string GameId { get; }
    IGameSession CreateSession();
}

public interface IGameSession : IDisposable
{
    void Start();
    string ExecuteCommand(string command, string argument);
}

public sealed record GameDescriptor(string Id, string Title, string EntryAssembly, string EntryType);

/// <summary>Product policy provided by the C# host, including persisted selection.</summary>
public interface IGameCatalog
{
    string SelectedGame { get; }
    IReadOnlyList<GameDescriptor> Games { get; }
    bool IsAvailable(string gameId);
    void Select(string gameId);
}

public static class GameHostServices
{
    public static IGameCatalog Catalog { get; set; } = null!;
}
