using System.Reflection;
using GameHost.Contracts;
using UnrealSharp.Plugins;

namespace GameHost;

internal static class GameModuleLoader
{
    // Ordinary UnrealSharp load-order and hot reload own both code variants.
    // The project selects a session, without creating another loader/context.
    public static IGameModule Load(GameDescriptor game)
    {
        Assembly assembly = PluginLoader.FindPlugin(game.EntryAssembly)?.Assembly?.Target as Assembly
            ?? throw new InvalidOperationException("Game assembly is unavailable: " + game.EntryAssembly + ". Build the managed solution before starting the editor.");
        Type entry = assembly.GetType(game.EntryType, throwOnError: true)!;
        var module = Activator.CreateInstance(entry) as IGameModule
            ?? throw new InvalidOperationException("The game entry does not implement IGameModule.");
        if (!module.GameId.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The game entry does not match the selected game.");
        return module;
    }
}
