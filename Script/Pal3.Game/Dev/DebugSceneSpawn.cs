using Pal3.Core.Contract.Enums;
using Pal3.Core.DataReader.Nav;
using Pal3.Core.DataReader.Scn;

namespace Pal3.Game.Dev;

internal static class DebugSceneSpawn
{
    internal static (int X, int Y, int Layer) Find(ScnFile scene, NavFile nav)
    {
        for (int layerIndex = 0; layerIndex < nav.Layers.Length; layerIndex++)
        {
            var layer = nav.Layers[layerIndex];
            var triggers = scene.ObjectInfos
                .Where(obj => obj.LayerIndex == layerIndex &&
                    obj.Type is (SceneObjectType.AutoTrigger or SceneObjectType.Door) &&
                    !obj.TileMapTriggerRect.IsEmpty)
                .Select(obj => obj.TileMapTriggerRect).ToArray();
            int centerX = layer.Width / 2;
            int centerY = layer.Height / 2;
            long bestDistance = long.MaxValue;
            int bestX = 0, bestY = 0;

            for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
            {
                var tile = layer.Tiles[x + y * layer.Width];
                if (!tile.IsWalkable() || !float.IsFinite(tile.GameBoxYPosition)) continue;
                long dx = x - centerX, dy = y - centerY;
                long distance = dx * dx + dy * dy;
                if (distance >= bestDistance || triggers.Any(rect => rect.IsPointInsideRect(x, y))) continue;

                bestDistance = distance;
                bestX = x;
                bestY = y;
            }

            if (bestDistance != long.MaxValue) return (bestX, bestY, layerIndex);
        }

        throw new InvalidOperationException("该场景找不到可行走的落点。");
    }
}
