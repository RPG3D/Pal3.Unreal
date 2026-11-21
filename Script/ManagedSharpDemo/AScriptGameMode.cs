using System;
using GameHost;
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace ManagedSharpDemo
{
    [UClass]
    public partial class AScriptGameMode : AGameModeBase
    {
        public AScriptGameMode()
        {
            DefaultPawnClass = typeof(ASpectatorPawn);
        }

        public override void BeginPlay()
        {
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo", "[PIE-Diag] GameMode BeginPlay dispatched, world=" + (UGameplayStatics.GetPlayerController(0) != null ? "has-player" : "no-player"));
            base.BeginPlay();
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo", "[PIE-Diag] base.BeginPlay done");

            // PIE fix: spawning the driver inside GameModeBase::BeginPlay deadlocks the
            // PIE world's begin-play dispatch. Schedule the spawn for the next tick —
            // the world is playing by then and the spawn runs outside the dispatch.
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo",
                "[PIE-Diag] GameMode BeginPlay done (driver spawn deferred to next tick)");
            UnrealSharp.Engine.SystemLibrary.SetTimerForNextTick(this, "SpawnGameHostNextTick");
        }

        [UFunction]
        public void SpawnGameHostNextTick()
        {
            try
            {
                TSubclassOf<AGameHostDriver> driverClass = new(typeof(AGameHostDriver));
                AGameHostDriver? driver = UnrealSharp.Engine.UGameplayStatics.SpawnActor(
                    actorType: driverClass,
                    spawnMethod: UnrealSharp.Engine.ESpawnActorCollisionHandlingMethod.AlwaysSpawn);

                UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo",
                    "[PIE-Diag] driver spawn: " + (driver == null ? "NULL" : "OK"));
            }
            catch (Exception e)
            {
                UnrealSharp.Log.UnrealLogger.LogError("ManagedSharpDemo",
                    "[PIE-Diag] driver spawn failed: " + e.GetType().Name + " " + e.Message);
            }
        }
    }
}
