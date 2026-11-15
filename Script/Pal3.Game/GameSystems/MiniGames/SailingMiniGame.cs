// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

#if PAL3

namespace Pal3.Game.GameSystems.MiniGames
{
    using System;
    using Command;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Engine.Services;
    using Scene;

    public sealed class SailingMiniGame : IDisposable,
        ICommandExecutor<MiniGameStartSailingCommand>
    {
        public SailingMiniGame()
        {
            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
        }

        public void Execute(MiniGameStartSailingCommand command)
        {
            var scenes = ServiceLocator.Instance.Get<SceneManager>();
            switch (command.StartSegment)
            {
                case 0:
                    scenes.BeginLoadScene("m04", "1", () =>
                    {
                        Pal3.Instance.Execute(new ActorActivateCommand(ActorConstants.PlayerActorVirtualID, 0));
                        Finish();
                    });
                    break;
                case 1:
                    scenes.BeginLoadScene("m05", "1", Finish);
                    break;
                default:
                    Finish();
                    break;
            }

            void Finish()
            {
                Pal3.Instance.Execute(new ScriptExecuteCommand(command.EndScriptId));
                Pal3.Instance.Execute(new UIDisplayNoteCommand("行船小游戏暂未实现，现已跳过"));
            }
        }
    }
}

#endif
