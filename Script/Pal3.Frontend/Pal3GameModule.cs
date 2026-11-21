using GameHost.Contracts;
using UnrealSharp;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace ManagedSharpDemo;

public sealed class Pal3GameModule : IGameModule
{
    public string GameId => Pal3.Game.Constants.GameConstants.AppName.ToLowerInvariant();

    public IGameSession CreateSession() => new Pal3Session();

    private sealed class Pal3Session : IGameSession
    {
        private APal3Driver? _driver;

        public void Start()
        {
            _driver = UGameplayStatics.SpawnActor(actorType: new TSubclassOf<APal3Driver>(typeof(APal3Driver)),
                spawnMethod: ESpawnActorCollisionHandlingMethod.AlwaysSpawn)
                ?? throw new InvalidOperationException("Could not create the PAL3 session driver.");
        }

        public string ExecuteCommand(string command, string argument) =>
            _driver?.ExecutePal3Command(command, argument)
            ?? throw new InvalidOperationException("The game session has not started.");

        public void Dispose()
        {
            if (_driver is { } driver && driver.IsValid()) driver.DestroyActor();
            _driver = null;
        }
    }
}
