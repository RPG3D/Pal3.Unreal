using Engine.Core.Abstraction;
using UnrealSharp.Engine;

namespace Engine.Core.Implementation
{
    public static class UEGameEntityFactory
    {
        public static IGameEntity Wrap(AActor actor) => new UEGameEntity(actor);
        public static IGameEntity Create(UWorld world, string name) { throw new NotImplementedException("Wire via UWorld.SpawnActor in Phase 4."); }
    }
}
