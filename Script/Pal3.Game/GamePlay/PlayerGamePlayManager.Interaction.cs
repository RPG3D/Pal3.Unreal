// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GamePlay
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Actor;
    using Actor.Controllers;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Engine.Core.Abstraction;
    using Engine.Extensions;
    using Engine.Logging;
    using Scene;
    using Scene.SceneObjects;
    using Script;
    using Script.Waiter;
    using State;
    using UnityEngine.InputSystem;

    using Quaternion = UnityEngine.Quaternion;
    using Bounds = UnityEngine.Bounds;
    using Vector2 = UnityEngine.Vector2;
    using Vector3 = UnityEngine.Vector3;

    public partial class PlayerGamePlayManager :
        ICommandExecutor<PlayerInteractionRequest>,
        ICommandExecutor<PlayerInteractWithObjectCommand>
    {
        public void Execute(PlayerInteractWithObjectCommand command)
        {
            GameScene currentScene = _sceneManager.GetCurrentScene();
            if (currentScene.GetAllActivatedSceneObjects().Contains(command.SceneObjectId))
            {
                Pal3.Instance.StartCoroutine(InteractWithSceneObjectAsync(
                    currentScene.GetSceneObject(command.SceneObjectId), startedByPlayer: false));
            }
            else
            {
                EngineLogger.LogError($"Scene object not found or not activated yet: {command.SceneObjectId}");
            }
        }

        public void Execute(PlayerInteractionRequest _)
        {
            if (TryCrossRiver()) return;

            if (IsPlayerActorInsideJumpableArea())
            {
                Pal3.Instance.StartCoroutine(JumpAsync());
            }
            else
            {
                InteractWithFacingInteractable();
            }
        }

        private void InteractionPerformed(InputAction.CallbackContext _)
        {
            if (!_playerActorManager.IsPlayerActorControlEnabled() ||
                !_playerActorManager.IsPlayerInputEnabled())
            {
                return;
            }

            Execute(new PlayerInteractionRequest());
        }

        public readonly record struct InteractionTarget(int Id, bool IsActor, Vector3 HintPosition);

        /// <summary>
        /// Interact with the nearby interactable object by the player actor facing direction.
        /// </summary>
        private void InteractWithFacingInteractable()
        {
            if (GetInteractionTarget() is not { } target) return;
            var scene = _sceneManager.GetCurrentScene();
            Pal3.Instance.StartCoroutine(target.IsActor
                ? InteractWithActorAsync(target.Id, scene.GetActorGameEntity(target.Id))
                : InteractWithSceneObjectAsync(scene.GetSceneObject(target.Id), startedByPlayer: true));
        }

        public InteractionTarget? GetInteractionTarget()
        {
            var scene = _sceneManager.GetCurrentScene();
            if (_sceneManager.IsLoading || scene == null || _playerActorGameEntity.IsNullOrDisposed() ||
                IsPlayerActorInsideJumpableArea()) return null;

            Vector3 actorFacingDirection = _playerActorMovementController.Transform.Forward;
            Vector3 actorCenterPosition = _playerActorActionController.GetRendererBounds().center;
            int currentLayerIndex = _playerActorMovementController.GetCurrentLayerIndex();

            float nearestInteractableFacingAngle = 181f;
            InteractionTarget? target = null;

            foreach (int sceneObjectId in scene.GetAllActivatedSceneObjects())
            {
                SceneObject sceneObject = scene.GetSceneObject(sceneObjectId);
                if (sceneObject.ObjectInfo.LayerIndex != currentLayerIndex || !sceneObject.IsDirectlyInteractable(0f)) continue;
                Bounds bounds = sceneObject.GetRendererBounds();
                Vector3 closestPoint = bounds.ClosestPoint(actorCenterPosition);
                if (!sceneObject.IsDirectlyInteractable(Vector3.Distance(actorCenterPosition, closestPoint))) continue;
                Consider(sceneObjectId, false, bounds, closestPoint);
            }

            foreach (KeyValuePair<int, IGameEntity> actorInfo in scene.GetAllActorGameEntities())
            {
                ActorController actorController = actorInfo.Value.GetComponent<ActorController>();
                ActorActionController actorActionController = actorInfo.Value.GetComponent<ActorActionController>();
                ActorMovementController actorMovementController = actorInfo.Value.GetComponent<ActorMovementController>();

                if (actorMovementController.GetCurrentLayerIndex() != currentLayerIndex ||
                    actorInfo.Key == _playerActorManager.GetPlayerActorId() ||
                    !actorController.IsActive || !actorController.IsDirectlyInteractable(0f)) continue;
                Bounds bounds = actorActionController.GetRendererBounds();
                if (!actorController.IsDirectlyInteractable(Vector3.Distance(actorCenterPosition, bounds.center))) continue;
                Consider(actorInfo.Key, true, bounds, bounds.center);
            }

            return target;

            void Consider(int id, bool isActor, Bounds bounds, Vector3 point)
            {
                Vector3 offset = point - actorCenterPosition;
                float facingAngle = Vector2.Angle(
                    new Vector2(actorFacingDirection.x, actorFacingDirection.z),
                    new Vector2(offset.x, offset.z));
                if (facingAngle >= nearestInteractableFacingAngle) return;
                nearestInteractableFacingAngle = facingAngle;
                target = new InteractionTarget(id, isActor, bounds.center + Vector3.up * (bounds.extents.y + .25f));
            }
        }

        private IEnumerator InteractWithSceneObjectAsync(SceneObject sceneObject, bool startedByPlayer)
        {
            Guid correlationId = Guid.NewGuid();
            bool requiresStateChange = sceneObject.ShouldGoToCutsceneWhenInteractionStarted();

            if (requiresStateChange)
            {
                _gameStateManager.TryGoToState(GameState.Cutscene);
                _gameStateManager.AddGamePlayStateLocker(correlationId);
            }

            yield return sceneObject.InteractAsync(new InteractionContext
            {
                CorrelationId = correlationId,
                InitObjectId = sceneObject.ObjectInfo.Id,
                PlayerActorGameEntity = _playerActorGameEntity,
                CurrentScene = _sceneManager.GetCurrentScene(),
                StartedByPlayer = startedByPlayer,
            });

            if (requiresStateChange)
            {
                _gameStateManager.RemoveGamePlayStateLocker(correlationId);
                _gameStateManager.TryGoToState(GameState.Gameplay);
            }
        }

        private IEnumerator InteractWithActorAsync(int actorId, IGameEntity actorGameEntity)
        {
            Pal3.Instance.Execute(new GameStateChangeRequest(GameState.Cutscene));
            Pal3.Instance.Execute(new ActorStopActionAndStandCommand(ActorConstants.PlayerActorVirtualID));

            GameActor targetActor = _sceneManager.GetCurrentScene().GetActor(actorId);
            Quaternion rotationBeforeInteraction = actorGameEntity.Transform.Rotation;

            ActorController actorController = actorGameEntity.GetComponent<ActorController>();
            ActorMovementController movementController = actorGameEntity.GetComponent<ActorMovementController>();

            // Pause current path follow movement of the interacting actor
            if (actorController != null &&
                movementController != null &&
                actorController.GetCurrentBehaviour() == ActorBehaviourType.PathFollow)
            {
                movementController.PauseMovement();
            }

            // Look at the target actor
            Pal3.Instance.Execute(new ActorLookAtActorCommand(_playerActor.Id, targetActor.Id));

            bool shouldResetFacingAfterInteraction = false;

            // Only let target actor look at player actor when NoTurn is set to false
            // and InitBehaviour is not set to Hold
            if (targetActor.Info.NoTurn == 0 && targetActor.Info.InitBehaviour != ActorBehaviourType.Hold)
            {
                Pal3.Instance.Execute(new ActorLookAtActorCommand(targetActor.Id, _playerActor.Id));
                shouldResetFacingAfterInteraction = true;
            }

            // Resume animation of the target actor if it is set to hold and loop action is set to 0
            if (targetActor.Info.InitBehaviour == ActorBehaviourType.Hold &&
                targetActor.Info.LoopAction == 0)
            {
                ActorActionController actionController = actorGameEntity.GetComponent<ActorActionController>();
                // PerformAction internally checks if the target actor is already performing the action
                actionController.PerformAction(actionController.GetCurrentAction(), false, 1);
            }

            // Run dialogue script
            uint scriptId = targetActor.GetScriptId();
            var waiter = new WaitUntilScriptFinished(ScriptManager.GetScriptType(scriptId), scriptId);
            Pal3.Instance.Execute(new ScriptExecuteCommand((int)scriptId));

            // Wait until the dialogue script is finished
            yield return waiter;

            // Resume current path follow movement of the interacting actor
            if (!actorGameEntity.IsNullOrDisposed() && actorController != null &&
                movementController != null &&
                actorController.GetCurrentBehaviour() == ActorBehaviourType.PathFollow)
            {
                movementController.ResumeMovement();
            }

            // Reset facing rotation of the interacting actor if needed
            if (shouldResetFacingAfterInteraction && !actorGameEntity.IsNullOrDisposed())
            {
                actorGameEntity.Transform.Rotation = rotationBeforeInteraction;
            }
        }
    }
}
