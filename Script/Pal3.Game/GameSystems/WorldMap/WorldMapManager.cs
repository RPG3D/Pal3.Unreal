// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.WorldMap
{
    using System;
    using System.Collections.Generic;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Core.Utilities;
    using Input;
    using Script;
    using Scene;
    using State;
    using UnityEngine.InputSystem;

    public sealed class WorldMapManager : IDisposable,
        ICommandExecutor<WorldMapEnableRegionCommand>,
        ICommandExecutor<ResetGameStateCommand>,
        ICommandExecutor<GameSwitchRenderingStateCommand>,
        ICommandExecutor<GameSwitchToMainMenuCommand>,
        ICommandExecutor<ToggleWorldMapRequest>
    {
        private readonly GameStateManager _gameStateManager;
        private readonly PlayerInputActions _playerInputActions;
        private readonly ScriptManager _scriptManager;
        private readonly SceneManager _sceneManager;
        private bool _isVisible;

        private readonly Guid _stateLockerGuid = Guid.NewGuid();

        private readonly Dictionary<int, int> _regionEnablementInfo = new ();

        public WorldMapManager(GameStateManager gameStateManager,
            SceneManager sceneManager,
            InputManager inputManager,
            ScriptManager scriptManager)
        {
            _gameStateManager = Requires.IsNotNull(gameStateManager, nameof(gameStateManager));
            _sceneManager = Requires.IsNotNull(sceneManager, nameof(sceneManager));
            _scriptManager = Requires.IsNotNull(scriptManager, nameof(scriptManager));
            _playerInputActions = Requires.IsNotNull(inputManager, nameof(inputManager)).GetPlayerInputActions();

            _playerInputActions.Gameplay.ToggleWorldMap.performed += ToggleWorldMapOnPerformed;
            _playerInputActions.UI.ToggleWorldMap.performed += ToggleWorldMapOnPerformed;
            _playerInputActions.UI.ExitCurrentShowingMenu.performed += HideWorldMapOnPerformed;

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            Hide();
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
            _playerInputActions.Gameplay.ToggleWorldMap.performed -= ToggleWorldMapOnPerformed;
            _playerInputActions.UI.ToggleWorldMap.performed -= ToggleWorldMapOnPerformed;
            _playerInputActions.UI.ExitCurrentShowingMenu.performed -= HideWorldMapOnPerformed;
        }

        private void ToggleWorldMapOnPerformed(InputAction.CallbackContext _)
        {
            ToggleWorldMap();
        }

        private void HideWorldMapOnPerformed(InputAction.CallbackContext _)
        {
            Close();
        }

        public bool IsVisible()
        {
            return _isVisible;
        }

        public Dictionary<int, int> GetRegionEnablementInfo()
        {
            return _regionEnablementInfo;
        }

        private void ToggleWorldMap()
        {
            if (_sceneManager.GetCurrentScene() == null) return;

            if (_isVisible)
            {
                Close();
            }
            else if (_gameStateManager.GetCurrentState() == GameState.Gameplay)
            {
                // Only enable world map toggling in non-maze scenes.
                if (_sceneManager.GetCurrentScene().GetSceneInfo().SceneType != SceneType.Maze)
                {
                    Show();
                }
            }
        }

        private void Show()
        {
            if (_isVisible) return;
            if (_regionEnablementInfo.Count == 0)
            {
                Pal3.Instance.Execute(new UIDisplayNoteCommand("大地图尚未开启"));
                return;
            }

            _gameStateManager.TryGoToState(GameState.UI);
            // Keep UI active when the exit script finishes and requests Gameplay.
            _gameStateManager.AddGamePlayStateLocker(_stateLockerGuid);
            _isVisible = true;
            Pal3.Instance.Execute(new ActorStopActionAndStandCommand(ActorConstants.PlayerActorVirtualID));
        }

        public bool CanTravelTo(int region) =>
            (uint)region < (uint)WorldMapConstants.WorldMapRegions.Length &&
            _regionEnablementInfo.TryGetValue(region, out int flag) && flag == 2;

        public bool TryTravel(int region)
        {
            if (!_isVisible || !CanTravelTo(region) || !_scriptManager.AddScript((uint)(100 + region), true))
                return false;

            Pal3.Instance.Execute(new StopScriptMusicCommand());
            Close();
            return true;
        }

        public void Close()
        {
            if (!_isVisible) return;
            Hide();
            _gameStateManager.TryGoToState(GameState.Gameplay);
        }

        private void Hide()
        {
            if (!_isVisible) return;
            _isVisible = false;
            _gameStateManager.RemoveGamePlayStateLocker(_stateLockerGuid);
        }

        public void Execute(WorldMapEnableRegionCommand command)
        {
            _regionEnablementInfo[command.Region] = command.EnablementFlag;
        }

        public void Execute(ResetGameStateCommand command)
        {
            Hide();
            _regionEnablementInfo.Clear();
        }

        public void Execute(GameSwitchRenderingStateCommand command)
        {
            if ((RenderingState)command.State == RenderingState.WorldMap)
            {
                Show();
            }
        }

        public void Execute(ToggleWorldMapRequest command)
        {
            ToggleWorldMap();
        }

        public void Execute(GameSwitchToMainMenuCommand command)
        {
            Hide();
        }
    }
}