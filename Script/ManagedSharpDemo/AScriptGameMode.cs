using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.EnhancedInput;


namespace ManagedSharpDemo
{
    [UClass]
    public partial class AScriptGameMode : AGameModeBase
    {
        public AScriptGameMode()
        {
            // The Shinbi sample character is a placeholder; spawning it loads a
            // skeletal mesh on every launch. Default to a lightweight spectator pawn
            // (PAL3 drives its own camera) and keep the sample pawn behind a switch.
            bool bSamplePawn = Environment.GetEnvironmentVariable("SHARP_SAMPLE_PAWN") == "1";
            DefaultPawnClass = bSamplePawn ? typeof(AScriptCharacter) : typeof(ASpectatorPawn);
        }

        public override void BeginPlay()
        {
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo", "[PIE-Diag] GameMode BeginPlay dispatched, world=" + (Engine.Core.Implementation.UEGameEntityFactory.TryGetWorldContext() != null ? "has-ctx" : "no-ctx"));
            base.BeginPlay();
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo", "[PIE-Diag] base.BeginPlay done");

#if PAL3
            // PIE fix: spawning the driver inside GameModeBase::BeginPlay deadlocks the
            // PIE world's begin-play dispatch. Schedule the spawn for the next tick —
            // the world is playing by then and the spawn runs outside the dispatch.
            UnrealSharp.Log.UnrealLogger.LogWarning("ManagedSharpDemo",
                "[PIE-Diag] GameMode BeginPlay done (driver spawn deferred to next tick)");
            UnrealSharp.Engine.SystemLibrary.SetTimerForNextTick(this, "SpawnPal3DriverNextTick");
        }

        [UFunction]
        public void SpawnPal3DriverNextTick()
        {
            try
            {
                TSubclassOf<APal3Driver> driverClass = new(typeof(APal3Driver));
                APal3Driver? driver = UnrealSharp.Engine.UGameplayStatics.SpawnActor(
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
#endif
    }
}
