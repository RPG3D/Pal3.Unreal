// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GamePlay
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Core.DataReader.Nav;
    using Core.Primitives;
    using Engine.Animation;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.Coroutine;
    using Engine.Extensions;
    using Engine.Renderer;
    using Scene;
    using State;

    using Vector2 = UnityEngine.Vector2;
    using Vector2Int = UnityEngine.Vector2Int;
    using Vector3 = UnityEngine.Vector3;

    public partial class PlayerGamePlayManager
    {
        private const float MIN_JUMP_DISTANCE = 1.2f;
        private const float MAX_JUMP_DISTANCE = 8f;
        private const float MAX_JUMP_Y_DIFFERENTIAL = 3.5f;
        private const float JUMP_HEIGHT = 6f;

        private bool TryCrossRiver()
        {
            if (!TryGetRiverLanding(out Vector2Int landing, out int layer)) return false;

            var movement = _playerActorMovementController;
            Vector3 position = _sceneManager.GetCurrentScene().GetTilemap().GetWorldPosition(landing, layer);
            Vector3 direction = position - movement.GetWorldPosition();
            direction.y = 0f;
            movement.CancelMovement();
            _playerActorGameEntity.Transform.Forward = direction.normalized;
            _playerActorGameEntity.Transform.Position = position;
            _playerActorActionController.PerformAction(ActorActionType.Stand);
            return true;
        }

        public Vector3? GetRiverCrossingHint()
        {
            if (!TryGetRiverLanding(out _, out _)) return null;
            var bounds = _playerActorActionController.GetRendererBounds();
            return bounds.center + Vector3.up * (bounds.extents.y + .25f);
        }

        private bool TryGetRiverLanding(out Vector2Int landing, out int layer)
        {
            landing = default;
            layer = 0;
            var scene = _sceneManager.GetCurrentScene();
            if (scene == null || _sceneManager.IsLoading || _playerActorGameEntity.IsNullOrDisposed() ||
                _gameStateManager.GetCurrentState() != GameState.Gameplay ||
                !scene.GetSceneInfo().IsCity("m01")) return false;

            layer = _playerActorMovementController.GetCurrentLayerIndex();
            Vector2Int origin = _playerActorMovementController.GetTilePosition();
            Tilemap tilemap = scene.GetTilemap();
            if (!tilemap.TryGetTile(origin, layer, out var originTile) || !originTile.IsWalkable()) return false;

            float bestDistance = MAX_JUMP_DISTANCE * MAX_JUMP_DISTANCE;
            bool found = false;
            foreach (int id in scene.GetAllActivatedSceneObjects())
            {
                var info = scene.GetSceneObject(id).ObjectInfo;
                GameBoxRect rect = info.TileMapTriggerRect;
                if (info.Type != SceneObjectType.JumpableArea || info.LayerIndex != layer ||
                    !rect.IsPointInsideRect(origin.x, origin.y)) continue;

                // Each SCN region spans a short stretch of river. Flood its walkable
                // cells to exclude the bank the player is already standing on.
                var reachable = new HashSet<Vector2Int> { origin };
                var pending = new Queue<Vector2Int>();
                pending.Enqueue(origin);
                while (pending.TryDequeue(out var tile))
                {
                    foreach (var offset in RiverWalkOffsets)
                    {
                        var next = tile + offset;
                        if (rect.IsPointInsideRect(next.x, next.y) &&
                            tilemap.TryGetTile(next, layer, out var step) && step.IsWalkable() && reachable.Add(next))
                            pending.Enqueue(next);
                    }
                }

                Vector3 start = tilemap.GetWorldPosition(origin, layer);
                for (int y = rect.Top; y <= rect.Bottom; y++)
                for (int x = rect.Left; x <= rect.Right; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (reachable.Contains(candidate) || !tilemap.TryGetTile(candidate, layer, out var tile) ||
                        !tile.IsWalkable() || tile.DistanceToNearestObstacle < 2) continue;
                    Vector3 delta = tilemap.GetWorldPosition(candidate, layer) - start;
                    if (MathF.Abs(delta.y) > MAX_JUMP_Y_DIFFERENTIAL) continue;
                    float distance = delta.x * delta.x + delta.z * delta.z;
                    if (distance < MIN_JUMP_DISTANCE * MIN_JUMP_DISTANCE || distance >= bestDistance) continue;
                    bestDistance = distance;
                    landing = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static readonly Vector2Int[] RiverWalkOffsets =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
        };

        private int _jumpableAreaEnterCount;

        private IGameEntity _jumpIndicatorGameEntity;
        private AnimatedBillboardRenderer _jumpIndicatorRenderer;

        private void JumpToTapPosition()
        {
            if (!_lastInputTapPosition.HasValue) return;

            if (!_physicsManager.TryCameraRaycastFromScreenPoint(_lastInputTapPosition.Value,
                    out (Vector3 hitPoint, IGameEntity colliderGameEntity) hitResult)) return;

            if (hitResult.colliderGameEntity.GetComponent<NavMesh>() != null)
            {
                Pal3Game.Instance.StartCoroutine(JumpAsync(hitResult.hitPoint));
            }
        }

        public void PlayerActorEnteredJumpableArea()
        {
            if (_jumpIndicatorGameEntity.IsNullOrDisposed())
            {
                ISprite[] sprites = _resourceProvider.GetJumpIndicatorSprites();

                _jumpIndicatorGameEntity = GameEntityFactory.Create($"JumpIndicator",
                    _playerActorGameEntity, worldPositionStays: false);
                _jumpIndicatorGameEntity.Transform.LocalScale = new Vector3(3f, 3f, 3f);
                _jumpIndicatorGameEntity.Transform.LocalPosition = new Vector3(0f,
                    _playerActorActionController.GetActorHeight() + 1f, 0f);

                _jumpIndicatorRenderer = _jumpIndicatorGameEntity.AddComponent<AnimatedBillboardRenderer>();
                _jumpIndicatorRenderer.Init(sprites, 4);
            }

            if (_jumpableAreaEnterCount == 0)
            {
                _jumpIndicatorRenderer.StartAnimation(-1);
            }

            _jumpableAreaEnterCount++;
        }

        public void PlayerActorExitedJumpableArea()
        {
            _jumpableAreaEnterCount--;

            if (_jumpableAreaEnterCount == 0 && _jumpIndicatorRenderer != null)
            {
                _jumpIndicatorRenderer.StopAnimation();
            }
        }

        private bool IsPlayerActorInsideJumpableArea()
        {
            return _jumpableAreaEnterCount > 0;
        }

        private void ResetAndDisposeJumpIndicators()
        {
            _jumpableAreaEnterCount = 0;

            if (_jumpIndicatorRenderer != null)
            {
                _jumpIndicatorRenderer.StopAnimation();
                _jumpIndicatorRenderer.Destroy();
                _jumpIndicatorRenderer = null;
            }

            if (_jumpIndicatorGameEntity != null)
            {
                _jumpIndicatorGameEntity.Destroy();
                _jumpIndicatorGameEntity = null;
            }
        }

        private IEnumerator JumpAsync(Vector3? jumpTargetPosition = null)
        {
            _gameStateManager.TryGoToState(GameState.Cutscene);
            _playerActorMovementController.CancelMovement();

            Vector3 currentPosition = _playerActorMovementController.GetWorldPosition();

            GameScene currentScene = _sceneManager.GetCurrentScene();
            Tilemap tilemap = currentScene.GetTilemap();

            bool IsPositionCanJumpTo(Vector3 position, int layerIndex, out float yPosition, out int distanceToObstacle)
            {
                Vector2Int tilePosition = tilemap.GetTilePosition(position, layerIndex);

                if (tilemap.TryGetTile(position, layerIndex, out NavTile tile) &&
                    tile.IsWalkable())
                {
                    distanceToObstacle = tile.DistanceToNearestObstacle;
                    yPosition = tile.GameBoxYPosition.ToUnityYPosition();

                    if (MathF.Abs(yPosition - currentPosition.y) > MAX_JUMP_Y_DIFFERENTIAL) return false;

                    if (currentScene.IsPositionInsideJumpableArea(layerIndex, tilePosition))
                    {
                        return true;
                    }

                    #if PAL3
                    // Special case for PAL3 M15-B1
                    if (_sceneManager.GetCurrentScene().GetSceneInfo().Is("m15", "b1"))
                    {
                        return true;
                    }
                    #endif
                }

                yPosition = 0f;
                distanceToObstacle = 0;
                return false;
            }

            Vector3 jumpDirection = _playerActorGameEntity.Transform.Forward;

            if (jumpTargetPosition != null)
            {
                jumpDirection = jumpTargetPosition.Value - _playerActorGameEntity.Transform.Position;
                jumpDirection.y = 0f;
                jumpDirection.Normalize();
                _playerActorGameEntity.Transform.Forward = jumpDirection;
            }

            List<(Vector3 position, int layerIndex, int distanceToObstacle)> validJumpTargetPositions = new();

            for (float i = MIN_JUMP_DISTANCE; i <= MAX_JUMP_DISTANCE; i += 0.1f)
            {
                Vector3 targetPosition = currentPosition + jumpDirection * i;

                for (int j = 0; j < tilemap.GetLayerCount(); j++)
                {
                    if (IsPositionCanJumpTo(targetPosition, j,
                            out float yPosition, out int distanceToObstacle))
                    {
                        Vector3 position = targetPosition;
                        position.y = yPosition;
                        validJumpTargetPositions.Add((position, j, distanceToObstacle));
                    }
                }
            }

            int currentLayer = _playerActorMovementController.GetCurrentLayerIndex();
            int jumpTargetLayer = currentLayer;
            if (validJumpTargetPositions.Count > 0)
            {
                // If there are valid jump target positions in different layers,
                // only pick positions in the other layer
                if (validJumpTargetPositions.Any(_ => _.layerIndex != currentLayer))
                {
                    validJumpTargetPositions = validJumpTargetPositions.Where(_ => _.layerIndex != currentLayer).ToList();
                }

                // Pick a position that is farthest from obstacles
                int maxDistanceToObstacle = validJumpTargetPositions.Max(_ => _.distanceToObstacle);
                (jumpTargetPosition, jumpTargetLayer, _) = validJumpTargetPositions
                    .First(_ => _.distanceToObstacle == maxDistanceToObstacle);
            }
            else
            {
                jumpTargetPosition = currentPosition;
            }

            _playerActorActionController.PerformAction(ActorConstants.ActionToNameMap[ActorActionType.Jump],
                 overwrite: true, loopCount: 1);
            yield return CoroutineYieldInstruction.WaitForSeconds(0.7f);

            float xzOffset = Vector2.Distance(
                new Vector2(jumpTargetPosition.Value.x, jumpTargetPosition.Value.z),
                new Vector2(currentPosition.x, currentPosition.z));
            float startingYPosition = currentPosition.y;
            float yOffset = jumpTargetPosition.Value.y - currentPosition.y;

            yield return CoreAnimation.EnumerateValueAsync(0f, 1f, 1.1f, AnimationCurveType.Sine,
                value =>
                {
                    Vector3 calculatedPosition = currentPosition + jumpDirection * (xzOffset * value);
                    calculatedPosition.y = startingYPosition + (0.5f - MathF.Abs(value - 0.5f)) * JUMP_HEIGHT + yOffset * value;
                    _playerActorGameEntity.Transform.Position = calculatedPosition;
                });
            yield return CoroutineYieldInstruction.WaitForSeconds(0.7f);

            _playerActorMovementController.SetNavLayer(jumpTargetLayer);

            PlayerActorTilePositionChanged(jumpTargetLayer,
                tilemap.GetTilePosition(jumpTargetPosition.Value, jumpTargetLayer), false);

            _gameStateManager.TryGoToState(GameState.Gameplay);
        }
    }
}
