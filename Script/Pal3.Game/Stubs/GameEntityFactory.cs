// Stub implementations of engine-service types referenced by ported Pal3.Game code.
// These live in the GLOBAL namespace because the original Unity project placed
// GameEntityFactory in the root namespace and Pal3.Game.* files call it unqualified
// (no per-file `using Pal3.Game;`). Runtime behavior is deferred; these compile shims
// let the managed solution build so the editor can start.

using System;
using System.Collections.Generic;
using Engine.Core.Abstraction;
using Engine.Services;

/// <summary>
/// Global factory for creating game entities (mirrors the original root-namespace
/// GameEntityFactory). Real spawning is wired through Engine's UEGameEntityFactory
/// in a later phase; for now Create returns a stub <see cref="GameEntity"/> so
/// gameplay code compiles and runs up to the point of actual world spawning.
/// </summary>
public static class GameEntityFactory
{
    public static IGameEntity Create(string name)
        => new GameEntity(name);

    public static IGameEntity Create(string name, IGameEntity? parent, bool worldPositionStays = false)
    {
        var entity = new GameEntity(name);
        if (parent != null)
        {
            entity.SetParent(parent, worldPositionStays);
        }
        return entity;
    }

    public static IGameEntity Create(string name, object? prefab, IGameEntity? parent, bool worldPositionStays = false)
        => Create(name, parent, worldPositionStays);
}

/// <summary>
/// Minimal IGameEntity implementation. Wraps a UnityEngine.GameObject when created from
/// an existing scene object (e.g. the main camera); otherwise a fresh stub entity.
/// </summary>
public sealed class GameEntity : IGameEntity
{
    private readonly UnityEngine.GameObject _gameObject;

    public GameEntity(string name)
    {
        Name = name;
        _gameObject = null!;
    }

    public GameEntity(UnityEngine.GameObject gameObject)
    {
        _gameObject = gameObject;
        Name = gameObject?.name ?? "";
    }

    public string Name { get; set; }
    public bool IsStatic { get; set; }
    public int Layer { get; set; }
    public ITransform Transform { get; } = new StubTransform();

    public object NativeObject => (object?)_gameObject ?? this;
    public bool IsNativeObjectDisposed => false;

    public void Destroy() { }

    public void SetParent(IGameEntity parent, bool worldPositionStays) { }
    public T AddComponent<T>() where T : class => null!;
    public object AddComponent(Type type) => null!;
    public T GetComponent<T>() where T : class => null!;
    public T GetComponentInChildren<T>() where T : class => null!;
    public T[] GetComponentsInChildren<T>() where T : class => Array.Empty<T>();
    public T GetOrAddComponent<T>() where T : class => null!;
    public IGameEntity FindChild(string name) => null!;
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
        foreach ((ITexture2D texture, _) in _cache.Values)
        {
            texture?.Destroy();
        }
        _cache.Clear();
    }
}
