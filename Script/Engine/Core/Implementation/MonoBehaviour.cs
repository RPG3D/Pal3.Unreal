using System;
using System.Collections;
using Engine.Core.Abstraction;
using Engine.Core.Implementation;

namespace UnityEngine
{
    public class Coroutine
    {
        internal object? Handle;
    }

    /// <summary>Component lifecycle and coroutine compatibility for managed UE entities.</summary>
    public class MonoBehaviour : Component
    {
        private IGameEntity? _owner;
        private bool _destroyed;

        public event Action? EnabledChanged;

        public override bool enabled
        {
            get => base.enabled;
            set
            {
                if (base.enabled == value) return;
                base.enabled = value;
                EnabledChanged?.Invoke();
            }
        }

        public bool isActiveAndEnabled => _owner != null && !_destroyed && enabled;

        public virtual void AttachToGameEntity(IGameEntity owner, GameObject gameObject)
        {
            if (_owner != null || _destroyed) throw new InvalidOperationException("The component is already attached or destroyed.");
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            BindGameObject(gameObject);
        }

        public void DetachFromGameEntity()
        {
            _destroyed = true;
            _owner = null;
        }

        public new T GetComponent<T>() where T : class => _owner?.GetComponent<T>()!;
        public new T GetComponentInChildren<T>() where T : class => _owner?.GetComponentInChildren<T>()!;
        public new T[] GetComponentsInChildren<T>() where T : class => _owner?.GetComponentsInChildren<T>() ?? Array.Empty<T>();

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (_owner == null || _destroyed) throw new InvalidOperationException("Attach the component before starting a coroutine.");
            return StartAttachedCoroutine(routine);
        }

        protected virtual Coroutine StartAttachedCoroutine(IEnumerator routine) => CoroutineScheduler.RunImmediately(routine, this);

        public void StopCoroutine(IEnumerator routine) => CoroutineScheduler.Stop(routine);
        public void StopCoroutine(Coroutine routine) => CoroutineScheduler.Stop(routine);
        public void StopAllCoroutines() => CoroutineScheduler.StopAll(this);
    }
}
