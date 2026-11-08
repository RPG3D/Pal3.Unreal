// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Caption
{
    using System;
    using System.Collections;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Utilities;
    using Data;
    using Engine.Animation;
    using Engine.Core.Abstraction;
    using Engine.Coroutine;
    using Input;
    using Script.Waiter;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.UI;
    using UnrealSharp.Engine;

    public sealed class CaptionRenderer : IDisposable,
        #if PAL3A
        ICommandExecutor<UIDisplayNextCaptionCommand>,
        #endif
        ICommandExecutor<UIDisplayCaptionCommand>
    {
        private const float FadeInSeconds = 10f;
        private const float AutoHoldSeconds = 2f;
        private const float FadeOutSeconds = .75f;

        private readonly GameResourceProvider _gameResourceProvider;
        private readonly PlayerInputActions _playerInputActions;
        private readonly Image _captionImage;

        private WaitUntilCanceled _skipCaptionWaiter;
        private bool _skipCaptionRequested;
        private ITexture2D _texture;
        private ISprite _sprite;

        public UTexture2D DisplayTexture => (_texture as UnityTexture2D)?.UeTexture;
        public float Opacity => _captionImage.color.a;

        public CaptionRenderer(GameResourceProvider gameResourceProvider,
            PlayerInputActions playerInputActions,
            Image captionImage)
        {
            _gameResourceProvider = Requires.IsNotNull(gameResourceProvider, nameof(gameResourceProvider));
            _playerInputActions = Requires.IsNotNull(playerInputActions, nameof(playerInputActions));
            _captionImage = Requires.IsNotNull(captionImage, nameof(captionImage));

            _captionImage.preserveAspect = true;
            _playerInputActions.Cutscene.Continue.performed += CutsceneContinueOnPerformed;

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            FinishCaption();
            _playerInputActions.Cutscene.Continue.performed -= CutsceneContinueOnPerformed;
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
        }

        private void CutsceneContinueOnPerformed(InputAction.CallbackContext obj)
            => RequestContinue();

        public void RequestContinue()
        {
            _skipCaptionRequested = true;
        }

        private void StartCaption(string textureName)
        {
            FinishCaption();
            _skipCaptionWaiter = new WaitUntilCanceled();
            Pal3Game.Instance.Execute(new ScriptRunnerAddWaiterRequest(_skipCaptionWaiter));
            Pal3Game.Instance.StartCoroutine(AnimateCaptionAsync(textureName, _skipCaptionWaiter));
        }

        private IEnumerator AnimateCaptionAsync(string textureName, WaitUntilCanceled waiter)
        {
            if (waiter != _skipCaptionWaiter) yield break;
            try
            {
                _texture = _gameResourceProvider.GetCaptionTexture(textureName);
                if (_texture == null) throw new InvalidOperationException("Caption texture is missing.");
                _sprite = _texture.CreateSprite(0f, 0f, _texture.Width, _texture.Height, 0.5f, 0.5f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not display PAL3 caption {textureName}: {e.Message}");
                FinishCaption();
            }
            if (waiter != _skipCaptionWaiter) yield break;

            _captionImage.sprite = _sprite.NativeObject as Sprite;
            _captionImage.color = Color.clear;

            yield return FadeAsync(waiter, 0f, 1f, FadeInSeconds);

            if (waiter != _skipCaptionWaiter) yield break;
            _skipCaptionRequested = false;
            yield return CoroutineYieldInstruction.WaitUntil(() =>
                waiter != _skipCaptionWaiter || _skipCaptionRequested || Pal3Boot.AutoDialogueEnabled);

            if (waiter != _skipCaptionWaiter) yield break;
            if (!_skipCaptionRequested)
                yield return CoroutineYieldInstruction.WaitForSeconds(AutoHoldSeconds);
            if (waiter != _skipCaptionWaiter) yield break;

            yield return FadeAsync(waiter, 1f, 0f, FadeOutSeconds);
            if (waiter == _skipCaptionWaiter) FinishCaption();
        }

        private IEnumerator FadeAsync(WaitUntilCanceled waiter, float from, float to, float seconds)
            => CoreAnimation.EnumerateValueAsync(from, to, seconds, AnimationCurveType.Linear, alpha =>
            {
                if (waiter == _skipCaptionWaiter) _captionImage.color = new Color(1f, 1f, 1f, alpha);
            });

        private void FinishCaption()
        {
            _captionImage.color = Color.clear;
            _captionImage.sprite = null;
            _texture?.Destroy();
            _sprite?.Destroy();
            _texture = null;
            _sprite = null;
            _skipCaptionWaiter?.CancelWait();
            _skipCaptionWaiter = null;
        }

        public void Execute(UIDisplayCaptionCommand command)
        {
            StartCaption(command.TextureName);
        }

        #if PAL3A
        public void Execute(UIDisplayNextCaptionCommand command)
        {
            StartCaption(command.TextureName);
        }
        #endif
    }
}
