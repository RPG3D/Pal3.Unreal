// UE-side driver actor for the managed Pal3 runtime layer.
// Bridges UE's per-frame tick into the managed world:
//   ReceiveTick -> GameEntityScriptRuntime.TickAll (coroutines + script lifecycle + Time state)
// Spawned by AScriptGameMode.BeginPlay. Deliberately free of gameplay logic -
// everything managed lives in Pal3.Game (see Stubs/GameEntityScriptRuntime.cs).

using System;
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using Engine.Core.Implementation;
using Pal3.Game;

namespace ManagedSharpDemo
{
    [UClass]
    public partial class APal3Driver : AActor
    {
        public APal3Driver()
        {
            // AActor ticks by default (CanEverTick/StartWithTickEnabled) - nothing to enable.
        }

        public override void BeginPlay()
        {
            base.BeginPlay();
            UnityEngine.Debug.Log("[APal3Driver] BeginPlay - starting managed runtime smoke probe");
            Pal3Boot.RunSmokeProbe();
            Pal3Boot.RunFullBoot();
        }

        public override void Tick(float deltaTime)
        {
            base.Tick(deltaTime);
            GameEntityScriptRuntime.TickAll(deltaTime);
            Pal3Boot.TickSmokeProbe(deltaTime);
        }
    }
}
