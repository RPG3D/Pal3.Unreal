// Stub implementations of engine-service types referenced by ported Pal3.Game code.
// These live in the GLOBAL namespace because the original Unity project placed
// GameEntityFactory in the root namespace and Pal3.Game.* files call it unqualified
// (no per-file `using Pal3.Game;`). Runtime behavior is deferred; these compile shims
// let the managed solution build so the editor can start.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Engine.Core.Abstraction;
using UnrealSharp.CoreUObject;
using Engine.Services;

/// <summary>
/// Global factory for creating game entities (mirrors the original root-namespace
/// GameEntityFactory). Real spawning is wired through Engine's UEGameEntityFactory
/// in a later phase; for now Create returns a stub <see cref="GameEntity"/> so
/// gameplay code compiles and runs up to the point of actual world spawning.
/// </summary>
/// <summary>
/// Implemented by stub components that have a real UE counterpart: the entity calls
/// AttachTo right after creating them so they can bind to the underlying actor.
/// </summary>
namespace Engine.Core.Implementation
{
    public interface IUeStubComponent
    {
        void AttachTo(UEGameEntity entity);
    }
}

public static class GameEntityFactory
{
    // Phase 4: when the managed runtime is attached to a UE world (APal3Driver
    // registered it), entities become real UE actors — transforms, names and
    // hierarchy land in the actual scene. Before that (or if a spawn fails),
    // stub entities keep pure-managed gameplay logic running unchanged.
    public static IGameEntity Create(string name)
        // ALWAYS the composite: it hosts managed scripts AND (when a UE world is
        // registered) an inner real actor. Returning the raw UEGameEntity here would
        // bypass the script host and make AddComponent<GameEntityScript> throw.
        => new GameEntity(name);

    public static IGameEntity Create(string name, IGameEntity? parent, bool worldPositionStays = false)
    {
        var entity = new GameEntity(name);
        if (parent != null && !entity.TrySetParent(parent, worldPositionStays))
        {
            entity.Destroy();
            throw new InvalidOperationException($"Could not attach entity '{name}' to its parent.");
        }
        return entity;
    }

    public static IGameEntity Create(string name, object? prefab, IGameEntity? parent, bool worldPositionStays = false)
        => Create(name, parent, worldPositionStays);
}

/// <summary>
/// Composite IGameEntity: hosts managed scripts (the GameEntityScript analog of
/// MonoBehaviours) and, when the UE runtime is attached, delegates the physical
/// side (transform, native UActorComponents, hierarchy, lifetime) to an inner
/// <see cref="Engine.Core.Implementation.UEGameEntity"/> wrapping a real AActor.
/// Without the UE side it degrades to the pure-managed stub — gameplay code sees
/// one class either way.
/// </summary>
public sealed class GameEntity : IGameEntity
{
    // Keep the owner for a hosted GameObject, including after teardown.
    // The weak key lets the pair be collected when the view is no longer used.
    private static readonly ConditionalWeakTable<UnityEngine.GameObject, GameEntity> Owners = new();
    private readonly UnityEngine.GameObject _gameObject;
    private readonly Engine.Core.Implementation.UEGameEntity? _ue;

    public GameEntity(string name)
    {
        Name = name;
        _gameObject = null!;
        // Phase 4: back the entity with a real UE actor whenever the driver has
        // registered a world. All transform/hierarchy/native traffic goes there.
        _ue = Engine.Core.Implementation.UEGameEntityFactory.TryCreate(name) as Engine.Core.Implementation.UEGameEntity;
        if (_ue != null) Name = name;
    }

    private GameEntity(UnityEngine.GameObject gameObject)
    {
        _gameObject = gameObject;
        Name = gameObject?.name ?? "";

        // Camera bridge: a stub GameObject that carries a real UE actor means the
        // game's ITransform writes (CameraManager drives it every frame) must land on
        // that actor instead of in-memory fields.
        if (gameObject?.UeActor is { } actor && actor != null && actor.IsValid())
        {
            _ue = Engine.Core.Implementation.UEGameEntityFactory.Wrap(actor) as Engine.Core.Implementation.UEGameEntity;
        }
    }

    internal static GameEntity FromGameObject(UnityEngine.GameObject gameObject)
    {
        var entity = Owners.GetValue(gameObject, static view => new GameEntity(view));
        _ = entity.UnityGameObject;
        return entity;
    }

    public string Name
    {
        get => _ue != null ? _ue.Name : _name;
        set
        {
            _name = value;
            if (_ue != null) _ue.Name = value;
        }
    }
    private string _name = "";

    public bool IsStatic
    {
        get => _ue?.IsStatic ?? _isStatic;
        set
        {
            _isStatic = value;
            if (_ue != null) _ue.IsStatic = value;
        }
    }
    private bool _isStatic;

    public int Layer { get; set; }

    public ITransform Transform => _ue?.Transform ?? _stubTransform;
    private readonly StubTransform _stubTransform = new();

    public object NativeObject => (object?)_ue?.NativeObject ?? (object?)_gameObject ?? this;
    public bool IsNativeObjectDisposed => _destroyed || (_ue?.IsNativeObjectDisposed ?? false);

    // Managed script components attached to this entity (MonoBehaviour analog).
    private readonly List<Engine.Core.Implementation.GameEntityScript> _scripts = new();
    private UnityEngine.GameObject? _unityGameObject;

    internal UnityEngine.GameObject UnityGameObject
    {
        get
        {
            if (_unityGameObject != null) return _unityGameObject;
            var gameObject = _gameObject ?? new UnityEngine.GameObject(Name)
            {
                UeActor = _ue?.NativeObject as UnrealSharp.Engine.AActor
            };
            _unityGameObject = gameObject;
            Owners.GetValue(gameObject, _ => this);
            gameObject.BindComponentHost(AddComponent, FindManagedComponent,
                type => EnumerateComponentsInChildren(type), new HostedTransform(this), Destroy);
            if (_destroyed) gameObject.ReleaseComponentHost();
            return gameObject;
        }
    }

    private sealed class HostedTransform(GameEntity entity) : UnityEngine.Transform
    {
        private GameEntity Entity => entity;

        public override UnityEngine.Vector3 position
        {
            get => entity.Transform.Position;
            set => entity.Transform.Position = value;
        }

        public override UnityEngine.Vector3 localPosition
        {
            get => entity.Transform.LocalPosition;
            set => entity.Transform.LocalPosition = value;
        }

        public override UnityEngine.Quaternion rotation
        {
            get => entity.Transform.Rotation;
            set => entity.Transform.Rotation = value;
        }

        public override UnityEngine.Vector3 localScale
        {
            get => entity.Transform.LocalScale;
            set => entity.Transform.LocalScale = value;
        }

        public override UnityEngine.Vector3 forward => entity.Transform.Forward;

        public override void SetParent(UnityEngine.Transform? parent, bool worldPositionStays = true)
        {
            if (parent == null) entity.SetParent(null, worldPositionStays);
            else if (parent is HostedTransform host) entity.SetParent(host.Entity, worldPositionStays);
        }

        public override void LookAt(UnityEngine.Vector3 target) => entity.Transform.LookAt(target);
    }

    // Unity-style components with no managed/UE equivalent (StaticMeshRenderer,
    // Collider, AudioSource, ...). They are plain stub objects; Unity semantics are
    // preserved: AddComponent creates + stores, GetComponent only looks up.
    private readonly List<object> _stubComponents = new();

    // Child entities created with this one as parent. Unity destroys the whole
    // subtree; without this the children's scripts keep ticking against actors that
    // the parent already destroyed (native use-after-free).
    private readonly List<GameEntity> _children = new();
    private GameEntity? _parent;
    private bool _destroyed;

    // Use only while the owning system is transitioning away from this subtree.
    internal IEnumerable<GameEntity> DestroyDescendantsInBatches()
    {
        foreach (GameEntity child in _children.ToArray())
        {
            foreach (GameEntity descendant in child.DestroyDescendantsInBatches()) yield return descendant;
            child.Destroy();
            yield return child;
        }
    }

    public void Destroy()
    {
        if (_destroyed) return;
        _destroyed = true;
        _parent?._children.Remove(this);
        _parent = null;

        // Unity destroys the whole subtree, and destroyed objects tolerate any later
        // access. Do the same: children first, then our own scripts/components, then
        // the actor (guarded — UETransform/UE wrappers no-op once the actor is gone).
        foreach (GameEntity child in _children.ToArray())
        {
            child.Destroy();
        }
        _children.Clear();

        foreach (Engine.Core.Implementation.GameEntityScript script in _scripts.ToArray())
        {
            Engine.Core.Implementation.GameEntityScriptRuntime.Deactivate(script);
        }
        _scripts.Clear();

        foreach (object component in _stubComponents)
        {
            if (component is UnityEngine.MonoBehaviour behaviour)
            {
                Engine.Core.Implementation.GameEntityScriptRuntime.Deactivate(behaviour);
            }
            if (component is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch (Exception) { }
            }
        }
        _stubComponents.Clear();
        _unityGameObject?.ReleaseComponentHost();

        _ue?.Destroy();
    }

    public void SetParent(IGameEntity? parent, bool worldPositionStays)
        => TrySetParent(parent, worldPositionStays);

    internal bool TrySetParent(IGameEntity? parent, bool worldPositionStays)
    {
        if (_destroyed || parent?.IsNativeObjectDisposed == true) return false;
        for (var ancestor = parent as GameEntity; ancestor != null; ancestor = ancestor._parent)
        {
            if (ReferenceEquals(this, ancestor)) return false;
        }

        // The UE side needs the parent's inner UEGameEntity: passing the composite
        // GameEntity made UEGameEntity.SetParent bail out ("parent is not UEGameEntity"),
        // so NO managed entity was ever attached to its parent actor. Scene geometry
        // hid the bug (world-space transforms), but actor models are positioned in
        // parent space and collapsed to the world origin — 景天 was invisible at the bed.
        Engine.Core.Implementation.UEGameEntity? parentUe = parent switch
        {
            GameEntity gameEntity => gameEntity._ue,
            Engine.Core.Implementation.UEGameEntity ueEntity => ueEntity,
            _ => null,
        };
        if (_ue != null)
        {
            if (parent != null && parentUe == null) return false;
            if (!_ue.TrySetParent(parentUe, worldPositionStays)) return false;
        }

        var managedParent = parent as GameEntity;
        if (!ReferenceEquals(_parent, managedParent))
        {
            _parent?._children.Remove(this);
            _parent = managedParent;
            _parent?._children.Add(this);
        }
        return true;
    }

    /// <summary>
    /// Unity AddComponent analog: managed GameEntityScript-derived components are
    /// instantiated, bound to this entity and activated (lifecycle dispatch +
    /// coroutine scheduling via GameEntityScriptRuntime). Other types are deferred.
    /// </summary>
    public T AddComponent<T>() where T : class
    {
        if (_destroyed) return null!;
        if (typeof(Engine.Core.Implementation.GameEntityScript).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)AttachScript(typeof(T));
        }
        // Native UE components (Collider, Rigidbody, ...) go to the actor — but only
        // once it exists; pre-attach stub phase has nothing to add onto.
        if (_ue != null && typeof(UnrealSharp.Engine.UActorComponent).IsAssignableFrom(typeof(T)))
        {
            return _ue.AddComponent<T>();
        }
        if (TryCreateStubComponent(typeof(T), out object? stub))
        {
            return (T)stub;
        }
        return null!;
    }

    public object AddComponent(Type type)
    {
        if (_destroyed) return null!;
        if (typeof(Engine.Core.Implementation.GameEntityScript).IsAssignableFrom(type))
        {
            return AttachScript(type);
        }
        if (_ue != null && typeof(UnrealSharp.Engine.UActorComponent).IsAssignableFrom(type))
        {
            return _ue.AddComponent(type);
        }
        return TryCreateStubComponent(type, out object? stub) ? stub : null!;
    }

    private bool TryCreateStubComponent(Type type, out object? component)
    {
        component = null;
        if (_destroyed) return false;
        if (type.IsAbstract || type.IsInterface || !type.IsClass) return false;
        if (type.GetConstructor(Type.EmptyTypes) == null) return false;
        try
        {
            component = Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            return false;
        }
        if (component is Engine.Core.Implementation.IUeStubComponent ueComponent && _ue != null)
        {
            ueComponent.AttachTo(_ue);
        }
        _stubComponents.Add(component!);
        if (component is UnityEngine.MonoBehaviour behaviour)
        {
            behaviour.AttachToGameEntity(this, UnityGameObject);
            Engine.Core.Implementation.GameEntityScriptRuntime.Activate(behaviour);
        }
        else if (component is UnityEngine.Component view)
        {
            UnityGameObject.BindComponent(view);
        }
        return true;
    }

    private Engine.Core.Implementation.GameEntityScript AttachScript(Type type)
    {
        var script = (Engine.Core.Implementation.GameEntityScript)Activator.CreateInstance(type)!;
        script.AttachToGameEntity(this, UnityGameObject);
        _scripts.Add(script);
        Engine.Core.Implementation.GameEntityScriptRuntime.Activate(script);
        return script;
    }

    public T GetComponent<T>() where T : class
    {
        if (FindManagedComponent(typeof(T)) is T component) return component;
        if (_ue != null && typeof(UnrealSharp.Engine.UActorComponent).IsAssignableFrom(typeof(T)))
        {
            return _ue.GetComponent<T>();
        }
        return null!;
    }

    private object? FindManagedComponent(Type type)
    {
        if (type == typeof(UnityEngine.Transform)) return _destroyed ? null : UnityGameObject.transform;
        foreach (Engine.Core.Implementation.GameEntityScript script in _scripts)
        {
            if (type.IsInstanceOfType(script)) return script;
        }
        foreach (object component in _stubComponents)
        {
            if (type.IsInstanceOfType(component)) return component;
        }
        return null;
    }

    public T GetComponentInChildren<T>() where T : class
    {
        if (_destroyed) return null!;
        if (GetComponent<T>() is { } component) return component;
        foreach (GameEntity child in _children)
        {
            if (child.GetComponentInChildren<T>() is { } match) return match;
        }
        return null!;
    }

    public T[] GetComponentsInChildren<T>() where T : class
    {
        List<T> result = new();
        Func<GameEntity, object?>? findNative = typeof(UnrealSharp.Engine.UActorComponent).IsAssignableFrom(typeof(T))
            ? static entity => entity._ue?.GetComponent<T>() : null;
        foreach (object component in EnumerateComponentsInChildren(typeof(T), findNative))
        {
            result.Add((T)component);
        }
        return result.ToArray();
    }

    private IEnumerable<object> EnumerateComponentsInChildren(Type type, Func<GameEntity, object?>? findNative = null)
    {
        if (_destroyed) yield break;
        if (type == typeof(UnityEngine.Transform)) yield return UnityGameObject.transform;
        foreach (Engine.Core.Implementation.GameEntityScript script in _scripts)
        {
            if (type.IsInstanceOfType(script)) yield return script;
        }
        foreach (object component in _stubComponents)
        {
            if (type.IsInstanceOfType(component)) yield return component;
        }
        if (findNative?.Invoke(this) is { } native) yield return native;
        foreach (GameEntity child in _children)
        {
            foreach (object component in child.EnumerateComponentsInChildren(type, findNative))
                yield return component;
        }
    }

    public T GetOrAddComponent<T>() where T : class
    {
        T existing = GetComponent<T>();
        if (existing != null) return existing;
        return AddComponent<T>();
    }
    public IGameEntity FindChild(string name)
    {
        foreach (GameEntity child in _children)
        {
            if (!child.IsNativeObjectDisposed && child._name == name)
            {
                return child;
            }
        }
        return null!;
    }
}

/// <summary>
/// No-op ITransform for stub entities. Position/rotation stored in plain fields so
/// gameplay math that reads/writes transforms keeps working; nothing is applied to
/// the UE scene.
/// </summary>
internal sealed class StubTransform : ITransform
{
    public UnityEngine.Vector3 Position { get; set; }
    public UnityEngine.Vector3 LocalPosition { get; set; }
    public UnityEngine.Quaternion Rotation { get; set; }
    public UnityEngine.Quaternion LocalRotation { get; set; }
    public UnityEngine.Vector3 LocalScale { get; set; } = UnityEngine.Vector3.one;
    public UnityEngine.Vector3 Forward { get; set; } = UnityEngine.Vector3.forward;
    public UnityEngine.Vector3 Right { get; set; } = UnityEngine.Vector3.right;
    public UnityEngine.Vector3 Up { get; set; } = UnityEngine.Vector3.up;
    public UnityEngine.Vector3 EulerAngles { get; set; }

    public object NativeObject => this;
    public bool IsNativeObjectDisposed => false;
    public void Destroy() { }

    public void Translate(UnityEngine.Vector3 translation)
        => Position += translation;

    public void GetPositionAndRotation(out UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation)
    {
        position = Position;
        rotation = Rotation;
    }

    public void SetPositionAndRotation(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    public void GetLocalPositionAndRotation(out UnityEngine.Vector3 position, out UnityEngine.Quaternion rotation)
    {
        position = LocalPosition;
        rotation = LocalRotation;
    }

    public void SetLocalPositionAndRotation(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
    {
        LocalPosition = position;
        LocalRotation = rotation;
    }

    public void LookAt(UnityEngine.Vector3 target) { }
    public void RotateAround(UnityEngine.Vector3 centerPoint, UnityEngine.Vector3 axis, float angle) { }
}

/// <summary>
/// Stub physics manager. All raycast/boxcast queries return no hits.
/// </summary>
public sealed class PhysicsManager : IPhysicsManager
{
    public PhysicsManager(UnityEngine.Camera camera) { }

    public int MaxRaycastHitCount => 0;

    public bool TryCameraRaycastFromScreenPoint(UnityEngine.Vector2 screenPoint,
        out (UnityEngine.Vector3 hitPoint, IGameEntity colliderGameEntity) hitResult,
        float maxDistance = float.PositiveInfinity)
    {
        hitResult = default;
        return false;
    }

    public int CameraRaycastFromScreenPoint(UnityEngine.Vector2 screenPoint,
        (UnityEngine.Vector3 hitPoint, IGameEntity colliderGameEntity)[] hitResults,
        float maxDistance = float.PositiveInfinity) => 0;

    public int BoxCast(UnityEngine.Vector3 center,
        UnityEngine.Vector3 halfExtents,
        UnityEngine.Vector3 direction,
        UnityEngine.Quaternion orientation,
        (UnityEngine.Vector3 hitPoint, IGameEntity colliderGameEntity)[] hitResults) => 0;
}

/// <summary>
/// Simple dictionary-backed ITextureCache. Real UE texture resources are deferred;
/// this keeps texture bookkeeping consistent so game logic compiles and runs.
/// </summary>
public sealed class TextureCache : ITextureCache
{
    private readonly Dictionary<string, (ITexture2D texture, bool hasAlphaChannel)> _cache = new();

    public bool Add(string name, ITexture2D texture) => _cache.TryAdd(name, (texture, true));

    public bool Add(string name, ITexture2D texture, bool hasAlphaChannel) => _cache.TryAdd(name, (texture, hasAlphaChannel));

    public bool TryGet(string name, out ITexture2D texture)
    {
        if (_cache.TryGetValue(name, out var entry))
        {
            texture = entry.texture;
            return true;
        }
        texture = null!;
        return false;
    }

    public bool TryGet(string name, out (ITexture2D texture, bool hasAlphaChannel) result)
        => _cache.TryGetValue(name, out result);

    public bool TryGetValue(string name, out ITexture2D texture) => TryGet(name, out texture);

    public bool TryGetValue(string name, out (ITexture2D texture, bool hasAlphaChannel) result)
        => _cache.TryGetValue(name, out result);

    public void DisposeAll()
    {
        // Evict only the cache's references. UI and actors can share these textures.
        _cache.Clear();
    }
}
