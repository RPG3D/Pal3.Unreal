using System;
using System.Reflection;
using Engine.Core.Abstraction;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace Engine.Core.Implementation
{
    public class UEGameEntity : IGameEntity
    {
        readonly AActor _actor;
        ITransform? _transform;
        public AActor Actor => _actor;

        public object NativeObject => _actor;
        public bool IsNativeObjectDisposed => !_actor.IsValid();

        public UEGameEntity(AActor actor) { _actor = actor ?? throw new ArgumentNullException(nameof(actor)); }

        public string Name
        {
            get
            {
#if WITH_EDITOR
                return _actor.GetActorLabel(false);
#else
                return _actor.Name.ToString();
#endif
            }
            set
            {
#if WITH_EDITOR
                _actor.SetActorLabel(value, false);
#endif
            }
        }
        public bool IsStatic { get => _actor.RootComponent?.Mobility == EComponentMobility.Static; set { if (_actor.RootComponent is { } r) r.Mobility = value ? EComponentMobility.Static : EComponentMobility.Movable; } }
        public int Layer { get => 0; set { } }

        public ITransform Transform { get { _transform ??= new UETransform(_actor); return _transform; } }

        public void SetParent(IGameEntity? parent, bool worldStays)
        {
            if (parent == null || parent is UEGameEntity)
                TrySetParent((UEGameEntity?)parent, worldStays);
        }

        public bool TrySetParent(UEGameEntity? parent, bool worldStays)
        {
            if (IsNativeObjectDisposed || parent?.IsNativeObjectDisposed == true) return false;
            if (parent == null)
            {
                var detachRule = worldStays ? EDetachmentRule.KeepWorld : EDetachmentRule.KeepRelative;
                _actor.DetachFromActor(detachRule, detachRule, detachRule);
                return _actor.AttachParentActor == null;
            }
            var rule = worldStays ? EAttachmentRule.KeepWorld : EAttachmentRule.KeepRelative;
            return _actor.AttachToActor(parent._actor, new FName("None"), rule, rule, rule, false);
        }

        public T AddComponent<T>() where T : class { if (!typeof(UActorComponent).IsAssignableFrom(typeof(T))) throw new ArgumentException(typeof(T).Name); return (T)AddComponentInternal(typeof(T)); }
        public object AddComponent(Type t) { if (!typeof(UActorComponent).IsAssignableFrom(t)) throw new ArgumentException(t.Name); return AddComponentInternal(t); }
        public T GetComponent<T>() where T : class { if (!typeof(UActorComponent).IsAssignableFrom(typeof(T))) throw new ArgumentException(typeof(T).Name); return (T)GetComponentInternal(typeof(T)); }
        public T GetComponentInChildren<T>() where T : class => GetComponent<T>();
        public T[] GetComponentsInChildren<T>() where T : class => new[] { GetComponent<T>() };
        public T GetOrAddComponent<T>() where T : class { try { return GetComponent<T>(); } catch { return AddComponent<T>(); } }
        public IGameEntity FindChild(string n)
        {
            foreach (AActor c in _actor.Children)
            {
#if WITH_EDITOR
                if (c.GetActorLabel(false) == n) return new UEGameEntity(c);
#else
                if (c.Name.ToString() == n) return new UEGameEntity(c);
#endif
            }
            return null!;
        }
        public void Destroy() => _actor.DestroyActor();
        bool IManagedObject.Equals(IManagedObject? o) => o != null && o.NativeObject is AActor a && a == _actor;

        static readonly MethodInfo? _addMethod = typeof(AActor).GetMethod("AddComponentByClass", new[] { typeof(bool), typeof(FTransform) });
        static readonly MethodInfo? _getMethod = typeof(AActor).GetMethod("GetComponentByClass", Type.EmptyTypes);
        object AddComponentInternal(Type t) => (_addMethod?.MakeGenericMethod(t) ?? throw new InvalidOperationException()).Invoke(_actor, new object[] { false, new FTransform() })!;
        object GetComponentInternal(Type t) => (_getMethod?.MakeGenericMethod(t) ?? throw new InvalidOperationException()).Invoke(_actor, null)!;
    }
}
