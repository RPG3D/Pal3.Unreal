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
            DefaultPawnClass = typeof(AScriptCharacter);
        }

        public override void BeginPlay()
        {
            base.BeginPlay();

            // Spawn the managed-runtime driver actor (Pal3 coroutines + script lifecycle).
            // NOTE: the extension's default TSubclassOf parameter has a zero NativeClass
            // and NREs - the class must be passed explicitly.
            TSubclassOf<APal3Driver> driverClass = new(typeof(APal3Driver));
            APal3Driver? driver = UObject.SpawnActor(
                actorType: driverClass,
                spawnMethod: ESpawnActorCollisionHandlingMethod.AlwaysSpawn,
                owner: this);
            if (driver == null)
            {
                UnrealSharp.Log.UnrealLogger.LogError("ManagedSharpDemo", "Failed to spawn APal3Driver");
            }
        }
    }
}
