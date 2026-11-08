using System;
using System.Collections.Generic;
using UnrealSharp.CoreUObject;
using UnrealSharp.SharpDemo;

namespace Pal3.Game.Data;

// UE components and widgets keep their own native references. These leases
// cover the separate case where a transient asset is still owned by C#.
public static class Pal3TransientAssets
{
    private static readonly Dictionary<UObject, WeakReference<object>> Owners = new();
    private static readonly List<UObject> ReleasedAssets = new();
    private static int _lastCollection = CollectionCount;
    private static int? _sceneCollection;

    private static int CollectionCount => GC.CollectionCount(0) + GC.CollectionCount(GC.MaxGeneration);

    internal static void Track(object owner, UObject? asset)
    {
        if (asset != null)
            Owners.Add(asset, new WeakReference<object>(owner));
    }

    internal static void Release(UObject? asset)
    {
        if (asset != null && Owners.Remove(asset))
            UPal3AssetHelper.ReleaseTransientAsset(asset);
    }

    internal static void CollectAfterSceneUnload()
    {
        _sceneCollection = GC.CollectionCount(GC.MaxGeneration);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false);
    }

    public static void Tick()
    {
        int collection = CollectionCount;
        bool sceneCollectionFinished = _sceneCollection is int previous &&
            GC.CollectionCount(GC.MaxGeneration) != previous;
        if (collection == _lastCollection && !sceneCollectionFinished) return;
        _lastCollection = collection;

        foreach (var (asset, owner) in Owners)
        {
            if (!owner.TryGetTarget(out _))
                ReleasedAssets.Add(asset);
        }

        foreach (var asset in ReleasedAssets)
            Release(asset);
        ReleasedAssets.Clear();

        if (sceneCollectionFinished)
        {
            _sceneCollection = null;
            UPal3AssetHelper.RequestTransientAssetCollection();
        }
    }

    public static void ReleaseSession()
    {
        bool hadAssets = Owners.Count > 0;
        foreach (var asset in Owners.Keys)
            UPal3AssetHelper.ReleaseTransientAsset(asset);
        Owners.Clear();
        _sceneCollection = null;
        if (hadAssets) UPal3AssetHelper.RequestTransientAssetCollection();
    }
}
