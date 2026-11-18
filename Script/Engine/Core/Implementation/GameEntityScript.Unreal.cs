using System.Collections;
using Engine.Core.Abstraction;
using UnityEngine;

namespace Engine.Core.Implementation
{
    public abstract partial class GameEntityScript
    {
        private IGameEntity _gameEntity = null!;

        private partial IGameEntity ResolveGameEntity() => _gameEntity;

        public override void AttachToGameEntity(IGameEntity owner, GameObject gameObject)
        {
            base.AttachToGameEntity(owner, gameObject);
            _gameEntity = owner;
        }

        // PAL3 scene and animation setup completes before the next scheduler tick.
        protected override UnityEngine.Coroutine StartAttachedCoroutine(IEnumerator routine) => CoroutineScheduler.Run(routine, this);

        public new void Destroy() => _gameEntity?.Destroy();
    }
}
