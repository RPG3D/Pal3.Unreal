// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Script.Waiter
{
    using Command;
    using Command.Extensions;
    using Core.Command;
    using UnityEngine;

    public sealed class WaitUntilScriptFinished : CustomYieldInstruction,
        ICommandExecutor<ScriptFinishedRunningNotification>,
        ICommandExecutor<ScriptFailedToRunNotification>
    {
        public override bool keepWaiting => !_isFinished;
        public bool Failed { get; private set; }

        private readonly PalScriptType _scriptType;
        private readonly uint _scriptId;
        private bool _isFinished;

        public WaitUntilScriptFinished(PalScriptType scriptType, uint scriptId)
        {
            _scriptType = scriptType;
            _scriptId = scriptId;
            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Execute(ScriptFinishedRunningNotification command) =>
            Complete(command.ScriptType, command.ScriptId, failed: false);

        public void Execute(ScriptFailedToRunNotification command) =>
            Complete(command.ScriptType, command.ScriptId, failed: true);

        private void Complete(PalScriptType scriptType, uint scriptId, bool failed)
        {
            if (_scriptType == scriptType && _scriptId == scriptId)
            {
                _isFinished = true;
                Failed = failed;
                CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
            }
        }
    }
}
