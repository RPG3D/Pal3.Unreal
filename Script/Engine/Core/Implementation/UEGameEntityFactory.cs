using System;
using Engine.Core.Abstraction;
using UnrealSharp;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace Engine.Core.Implementation
{
    public static class UEGameEntityFactory
    {
        // The managed Pal3 runtime spawns into whatever world its driver actor lives
        // in. APal3Driver registers itself in BeginPlay; until then the global
        // GameEntityFactory falls back to stub entities (pure managed, invisible) so
        // early boot code never NREs. The context is only a validity gate —
        // UObject.SpawnActor resolves the actual world itself.
        static UObject? _worldContext;

        static int _createdCount;
        public static int CreatedCount => _createdCount;

        /// <summary>
        /// Actor the ported CameraManager drives. Registered by the game layer
        /// (Pal3Boot) so Engine-side code can special-case it (e.g. the
        /// PAL3_FREEZE_CAMERA diagnostic switch) without referencing Pal3.Game.
        /// </summary>
        public static AActor? CameraActor;

        public static void RegisterWorldContext(UObject? context) => _worldContext = context;

        /// <summary>
        /// Drops the previous world's context and counters (PIE session reset). The
        /// old actors are destroyed with their world; keeping the context would let
        /// early boot code spawn into a dead PIE world.
        /// </summary>
        public static void ResetForNewSession()
        {
            _worldContext = null;
            CameraActor = null;
            _createdCount = 0;
        }

        public static UObject? TryGetWorldContext() => _worldContext;

        public static IGameEntity Wrap(AActor actor) => new UEGameEntity(actor);

        /// <summary>
        /// Create a real UE actor while the driver context is alive; null when no
        /// context is registered yet (caller falls back to a stub entity).
        /// </summary>
        public static IGameEntity? TryCreate(string name)
        {
            UObject? context = _worldContext;
            if (context == null || !context.IsValid())
            {
                return null;
            }
            try
            {
                return Create(name);
            }
            catch (Exception e)
            {
                UnrealSharp.Log.UnrealLogger.LogError("Pal3",
                    $"[UEGameEntityFactory] Create('{name}') failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Spawn a plain UE actor with a scene-component root and wrap it as a game
        /// entity. The actor is intentionally empty of visuals — rendering comes later
        /// (texture → material → mesh phase); transforms, names and hierarchy are real
        /// from day one so ported gameplay positions are verifiable in the outliner.
        /// </summary>
        public static IGameEntity Create(string name)
        {
            TSubclassOf<AActor> actorClass = new(typeof(AActor));
            AActor actor = UObject.SpawnActor(
                actorType: actorClass,
                spawnTransform: default,
                spawnMethod: ESpawnActorCollisionHandlingMethod.AlwaysSpawn)
                ?? throw new InvalidOperationException("SpawnActor returned null");

            // A plain AActor has no root component — without one there is no transform.
            // AddComponentByClass creates AND registers the component; on an actor with
            // no root, AddInstanceComponent makes the new component the root itself.
            USceneComponent root = actor.AddComponentByClass<USceneComponent>(false, new FTransform());

            var entity = new UEGameEntity(actor);
            entity.Name = name;
            ++_createdCount;
            return entity;
        }
    }
}
