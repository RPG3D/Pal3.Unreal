// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.GameSystems.Dialogue
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Contract.Constants;
    using Core.Utilities;
    using Data;
    using Engine.Animation;
    using Engine.Coroutine;
    using Engine.Extensions;
    using Engine.Logging;
    using Engine.Services;
    using Engine.UI;
    using Input;
    using Scene;
    using Script.Waiter;
    using State;
    using TMPro;
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.UI;

    public sealed class DialogueManager : IDisposable,
        ICommandExecutor<DialogueRenderActorAvatarCommand>,
        ICommandExecutor<DialogueRenderTextCommand>,
        ICommandExecutor<DialogueAddSelectionsCommand>,
        ICommandExecutor<DialogueRenderTextWithTimeLimitCommand>,
        ICommandExecutor<ResetGameStateCommand>
    {
        private const float LIMIT_TIME_DIALOGUE_PLAYER_MAX_REACTION_TIME_IN_SECONDS = 4f;
        private const float DIALOGUE_SHOW_HIDE_ANIMATION_DURATION = 0.12f;
        private const float DIALOGUE_SHOW_HIDE_ANIMATION_Y_OFFSET = -30f;
        private const float DIALOGUE_FLASHING_ANIMATION_DURATION = 0.5f;

        private const string INFORMATION_TEXT_COLOR_HEX = "#ffff05";

        private readonly GameResourceProvider _resourceProvider;
        private readonly GameStateManager _gameStateManager;
        private readonly SceneManager _sceneManager;
        private readonly PlayerInputActions _inputActions;

        private readonly CanvasGroup _dialogueCanvasGroup;
        private readonly Image _dialogueBackgroundImage;
        private readonly RoundedFrostedGlassImage _backgroundFrostedGlassImage;

        private readonly Image _avatarImageLeft;
        private readonly Image _avatarImageRight;
        private readonly TextMeshProUGUI _dialogueTextLeft;
        private readonly TextMeshProUGUI _dialogueTextRight;
        private readonly TextMeshProUGUI _dialogueTextDefault;

        private Texture2D _avatarTexture;
        private bool _isDialoguePresenting;
        private bool _isSkipDialogueRequested;
        private bool _isDialogueRenderingAnimationInProgress;

        private int _lastSelectedButtonIndex;
        private WaitUntilCanceled _selectionWaiter;
        private GameState _selectionReturnState;
        private double _totalTimeUsedBeforeSkippingTheLastDialogue;
        private readonly Stopwatch _reactionTimer = new ();
        private CancellationTokenSource _flashingAnimationCts = new ();

        private DialogueRenderActorAvatarCommand _lastAvatarCommand;
        private readonly Queue<IEnumerator> _dialogueRenderQueue = new();

        public DialogueManager(GameResourceProvider resourceProvider,
            GameStateManager gameStateManager,
            SceneManager sceneManager,
            InputManager inputManager,
            CanvasGroup dialogueCanvasGroup,
            Image dialogueBackgroundImage,
            Image avatarImageLeft,
            Image avatarImageRight,
            TextMeshProUGUI textLeft,
            TextMeshProUGUI textRight,
            TextMeshProUGUI textDefault)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _gameStateManager = Requires.IsNotNull(gameStateManager, nameof(gameStateManager));
            _sceneManager = Requires.IsNotNull(sceneManager, nameof(sceneManager));

            _dialogueCanvasGroup = Requires.IsNotNull(dialogueCanvasGroup, nameof(dialogueCanvasGroup));
            _dialogueBackgroundImage = Requires.IsNotNull(dialogueBackgroundImage, nameof(dialogueBackgroundImage));
            _backgroundFrostedGlassImage = Requires.IsNotNull(dialogueBackgroundImage.GetComponent<RoundedFrostedGlassImage>(),
                nameof(dialogueBackgroundImage));

            _avatarImageLeft = Requires.IsNotNull(avatarImageLeft, nameof(avatarImageLeft));
            _avatarImageRight = Requires.IsNotNull(avatarImageRight, nameof(avatarImageRight));

            _dialogueTextLeft = Requires.IsNotNull(textLeft, nameof(textLeft));
            _dialogueTextRight = Requires.IsNotNull(textRight, nameof(textRight));
            _dialogueTextDefault = Requires.IsNotNull(textDefault, nameof(textDefault));


            _avatarImageLeft.preserveAspect = true;
            _avatarImageRight.preserveAspect = true;

            ResetUI();

            _inputActions = inputManager.GetPlayerInputActions();
            _inputActions.Cutscene.Continue.performed += SkipDialoguePerformed;

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
            _inputActions.Cutscene.Continue.performed -= SkipDialoguePerformed;
        }

        public int GetDialogueSelectionButtonIndex()
        {
            return _lastSelectedButtonIndex;
        }

        public bool PlayerReactedInTimeForLimitTimeDialogue()
        {
            return _totalTimeUsedBeforeSkippingTheLastDialogue < LIMIT_TIME_DIALOGUE_PLAYER_MAX_REACTION_TIME_IN_SECONDS;
        }

        private IEnumerator TypeSentenceAsync(TextMeshProUGUI textUI, string sentence, float waitSecondsBeforeRenderingChar)
        {
            object charTypingAnimationDelay = CoroutineYieldInstruction.WaitForSeconds(waitSecondsBeforeRenderingChar);

            textUI.text = string.Empty;

            yield return charTypingAnimationDelay;

            string richText = string.Empty;
            foreach (char letter in sentence.ToCharArray())
            {
                if (richText.Length > 0)
                {
                    if (letter.Equals('>') && richText.Contains('>'))
                    {
                        textUI.text += richText + letter;
                        richText = string.Empty;
                        yield return charTypingAnimationDelay;
                        continue;
                    }

                    richText += letter;
                    continue;
                }
                else if (letter.Equals('<'))
                {
                    richText += letter;
                    continue;
                }

                if (_isDialogueRenderingAnimationInProgress == false) yield break;
                textUI.text += letter;
                yield return charTypingAnimationDelay;
            }
        }

        public void Update(float deltaTime)
        {
            if (!_isDialoguePresenting && _dialogueRenderQueue.Count > 0)
            {
                _isDialoguePresenting = true;
               Pal3.Instance.StartCoroutine(_dialogueRenderQueue.Dequeue());
            }
        }

        private TextMeshProUGUI GetRenderingTextUI(bool isAvatarPresented, bool isRightAligned)
        {
            if (!isAvatarPresented) return _dialogueTextDefault;
            else return isRightAligned ? _dialogueTextRight : _dialogueTextLeft;
        }

        private IEnumerator RenderDialogueTextWithAnimationAsync(TextMeshProUGUI dialogueTextUI,
            string text,
            float waitSecondsBeforeRenderingChar = 0.04f)
        {
            if (waitSecondsBeforeRenderingChar < float.Epsilon)
            {
                dialogueTextUI.text = text;
            }
            else
            {
                _isDialogueRenderingAnimationInProgress = true;
                yield return null;
                yield return TypeSentenceAsync(dialogueTextUI, text, waitSecondsBeforeRenderingChar);
            }

            _isDialogueRenderingAnimationInProgress = false;
        }

        private IEnumerator RenderDialogueAndWaitAsync(string text,
            bool trackReactionTime,
            DialogueRenderActorAvatarCommand avatarCommand = null,
            Action onFinished = null)
        {
            Pal3.Instance.Execute(new DialogueRenderingStartedNotification());
            _totalTimeUsedBeforeSkippingTheLastDialogue = 0f;

            bool isRightAligned = true;
            bool isAvatarPresented = false;

            // Publish the page and portrait to the managed UI presenter.
            Dev.DialoguePresentation.Clear();

            if (avatarCommand != null &&
                _sceneManager.GetCurrentScene().GetActor(avatarCommand.ActorId) is { } actor &&
                _resourceProvider.GetActorAvatarSprite(actor.Name, avatarCommand.AvatarTextureName) is { } avatarSprite)
            {
                isRightAligned = avatarCommand.RightAligned == 1;

                if (isRightAligned)
                {
                    _avatarImageRight.color = Color.white;
                    _avatarImageRight.sprite = avatarSprite.NativeObject as Sprite;
                }
                else
                {
                    _avatarImageLeft.color = Color.white;
                    _avatarImageLeft.sprite = avatarSprite.NativeObject as Sprite;
                }

                Dev.DialoguePresentation.ShowAvatar(avatarSprite, isRightAligned);

                isAvatarPresented = true;
            }

            TextMeshProUGUI dialogueTextUI = GetRenderingTextUI(isAvatarPresented, isRightAligned);

            _dialogueBackgroundImage.enabled = true;
            _dialogueCanvasGroup.alpha = 0f;
            _dialogueCanvasGroup.enabled = true;
            _isSkipDialogueRequested = false;

            yield return PlayDialogueBackgroundPopAnimationAsync(true);
            _isSkipDialogueRequested = false; // Ignore skip request during dialogue rendering animation

            if (trackReactionTime) // Setup timer and start flashing animation
            {
                _reactionTimer.Restart();
                if (!_flashingAnimationCts.IsCancellationRequested)
                {
                    _flashingAnimationCts.Cancel();
                }
                _flashingAnimationCts = new CancellationTokenSource();
                Pal3.Instance.StartCoroutine(PlayDialogueBackgroundFlashingAnimationAsync(
                     duration: LIMIT_TIME_DIALOGUE_PLAYER_MAX_REACTION_TIME_IN_SECONDS,
                    _flashingAnimationCts.Token));
            }

            // Render dialogue text typing animation
            foreach (string dialogue in DialogueTextProcessor.GetSubDialoguesAsync(text))
            {
                Dev.DialoguePresentation.ShowText(dialogue);
                IEnumerator renderDialogue = RenderDialogueTextWithAnimationAsync(dialogueTextUI, dialogue);

                Pal3.Instance.StartCoroutine(renderDialogue);

                yield return WaitForDialoguePageAsync();

                if (_isDialogueRenderingAnimationInProgress)
                {
                    _isDialogueRenderingAnimationInProgress = false;
                    Pal3.Instance.StopCoroutine(renderDialogue);
                    dialogueTextUI.text = dialogue;
                    yield return WaitForDialoguePageAsync();
                }
            }

            if (trackReactionTime) // Stop flashing animation and timer
            {
                _flashingAnimationCts.Cancel();
                _reactionTimer.Stop();
                _totalTimeUsedBeforeSkippingTheLastDialogue = _reactionTimer.Elapsed.TotalSeconds;
                EngineLogger.LogWarning($"Reaction time: {_totalTimeUsedBeforeSkippingTheLastDialogue:0.00f}");
            }

            yield return PlayDialogueBackgroundPopAnimationAsync(false);

            ResetUI();

            _isDialoguePresenting = false;

            onFinished?.Invoke();
        }

        private IEnumerator PlayDialogueBackgroundFlashingAnimationAsync(float duration, CancellationToken cancellationToken)
        {
            double startTime = GameTimeProvider.Instance.TimeSinceStartup;
            float initialBlurAmount = _backgroundFrostedGlassImage.blurAmount;
            float initialTransparency = _backgroundFrostedGlassImage.transparency;
            const float minThresholdPercentage = 0.65f;

            while (!cancellationToken.IsCancellationRequested)
            {
                yield return CoreAnimation.EnumerateValueAsync(1f, minThresholdPercentage, DIALOGUE_FLASHING_ANIMATION_DURATION / 2f,
                    AnimationCurveType.Linear, value =>
                    {
                        _backgroundFrostedGlassImage.SetMaterialBlurAmount(initialBlurAmount * value);
                        _backgroundFrostedGlassImage.SetMaterialTransparency(initialTransparency * value);
                    }, cancellationToken);

                if (GameTimeProvider.Instance.TimeSinceStartup - startTime >= duration) break;

                yield return CoreAnimation.EnumerateValueAsync(minThresholdPercentage, 1f, DIALOGUE_FLASHING_ANIMATION_DURATION / 2f,
                    AnimationCurveType.Linear, value =>
                    {
                        _backgroundFrostedGlassImage.SetMaterialBlurAmount(initialBlurAmount * value);
                        _backgroundFrostedGlassImage.SetMaterialTransparency(initialTransparency * value);
                    }, cancellationToken);

                if (GameTimeProvider.Instance.TimeSinceStartup - startTime >= duration) break;
            }

            _backgroundFrostedGlassImage.SetMaterialBlurAmount(initialBlurAmount);
            _backgroundFrostedGlassImage.SetMaterialTransparency(initialTransparency);
        }

        private IEnumerator PlayDialogueBackgroundPopAnimationAsync(bool showDialogue)
        {
            const float yOffset = DIALOGUE_SHOW_HIDE_ANIMATION_Y_OFFSET;
            Transform dialogueCanvasGroupTransform = _dialogueCanvasGroup.transform;
            Vector3 finalPosition = dialogueCanvasGroupTransform.localPosition;
            Vector3 startPosition = finalPosition + new Vector3(0f, yOffset, 0);

            float startValue = showDialogue ? 0f : 1f;
            float endValue = showDialogue ? 1f : 0f;

            if (showDialogue)
            {
                dialogueCanvasGroupTransform.localPosition = startPosition;
            }

            float initialBlurAmount = _backgroundFrostedGlassImage.blurAmount;
            float initialTransparency = _backgroundFrostedGlassImage.transparency;
            yield return CoreAnimation.EnumerateValueAsync(startValue, endValue, DIALOGUE_SHOW_HIDE_ANIMATION_DURATION,
                AnimationCurveType.EaseIn, value =>
                {
                    _dialogueCanvasGroup.transform.localPosition = finalPosition + new Vector3(0f, yOffset * (1 - value), 0);
                    _dialogueCanvasGroup.alpha = value;
                    _backgroundFrostedGlassImage.SetMaterialBlurAmount(initialBlurAmount * value);
                    _backgroundFrostedGlassImage.SetMaterialTransparency(initialTransparency * value);
                });
            _backgroundFrostedGlassImage.SetMaterialBlurAmount(initialBlurAmount);
            _backgroundFrostedGlassImage.SetMaterialTransparency(initialTransparency);

            _dialogueCanvasGroup.transform.localPosition = finalPosition; // Always set to final position
            _dialogueCanvasGroup.alpha = showDialogue ? 1f : 0f;
        }

        private void ResetUI()
        {
            _dialogueCanvasGroup.alpha = 0f;
            _dialogueCanvasGroup.enabled = false;

            _dialogueTextLeft.text = string.Empty;
            _dialogueTextRight.text = string.Empty;
            _dialogueTextDefault.text = string.Empty;

            _dialogueBackgroundImage.enabled = false;

            _avatarImageLeft.color = new Color(0f, 0f, 0f, 0f);
            _avatarImageRight.color = new Color(0f, 0f, 0f, 0f);
            _avatarImageLeft.sprite = null;
            _avatarImageRight.sprite = null;

            Dev.DialoguePresentation.Clear();
        }

        private IEnumerator WaitForDialoguePageAsync()
        {
            const float autoAdvanceDelay = 2.5f;
            float elapsed = 0f;
            while (!_isSkipDialogueRequested)
            {
                yield return null;
                if (_isSkipDialogueRequested) break;

                if (_isDialogueRenderingAnimationInProgress || !Pal3Boot.AutoDialogueEnabled)
                {
                    elapsed = 0f;
                    continue;
                }

                elapsed += Time.deltaTime;
                if (elapsed >= autoAdvanceDelay) break;
            }
            _isSkipDialogueRequested = false;
        }

        private void SkipDialoguePerformed(InputAction.CallbackContext _)
            => RequestContinue();

        public void RequestContinue()
        {
            if (_selectionWaiter != null) return;
            _isSkipDialogueRequested = true;
        }

        public void Execute(DialogueRenderTextCommand command)
        {
            WaitUntilCanceled skipDialogueWaiter = new();
            Pal3.Instance.Execute(new ScriptRunnerAddWaiterRequest(skipDialogueWaiter));
            DialogueRenderActorAvatarCommand avatarCommand = _lastAvatarCommand;
            _dialogueRenderQueue.Enqueue(RenderDialogueAndWaitAsync(
                DialogueTextProcessor.GetDisplayText(command.DialogueText, INFORMATION_TEXT_COLOR_HEX),
                false,
                avatarCommand,
                () => skipDialogueWaiter.CancelWait()));
            _lastAvatarCommand = null;
        }

        public void Execute(DialogueRenderTextWithTimeLimitCommand command)
        {
            WaitUntilCanceled skipDialogueWaiter = new();
            Pal3.Instance.Execute(new ScriptRunnerAddWaiterRequest(skipDialogueWaiter));
            DialogueRenderActorAvatarCommand avatarCommand = _lastAvatarCommand;
            _dialogueRenderQueue.Enqueue(RenderDialogueAndWaitAsync(
                DialogueTextProcessor.GetDisplayText(command.DialogueText, INFORMATION_TEXT_COLOR_HEX),
                true,
                avatarCommand,
                () => skipDialogueWaiter.CancelWait()));
            _lastAvatarCommand = null;
        }

        public void Execute(DialogueRenderActorAvatarCommand command)
        {
            _lastAvatarCommand = command; // Save for later use, since avatar command is always
                                          // followed by text command and we will render avatar
                                          // when rendering text if avatar command is present
        }

        public void Execute(DialogueAddSelectionsCommand command)
        {
            if (command.Selections.Length == 0) return;
            if (_selectionWaiter != null)
            {
                EngineLogger.LogWarning("Ignoring a second dialogue choice while a choice is pending.");
                return;
            }

            string[] choices = new string[command.Selections.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = DialogueTextProcessor.GetSelectionDisplayText((string)command.Selections[i]);
            }

            _selectionReturnState = _gameStateManager.GetCurrentState();
            _gameStateManager.TryGoToState(GameState.UI);
            _selectionWaiter = new WaitUntilCanceled();
            Pal3.Instance.Execute(new ScriptRunnerAddWaiterRequest(_selectionWaiter));
            Dev.DialoguePresentation.ShowChoices(choices);
        }

        public bool TryChoose(int index)
        {
            if (_selectionWaiter == null || (uint)index >= Dev.DialoguePresentation.Choices.Count)
            {
                return false;
            }

            _lastSelectedButtonIndex = index;
            _selectionWaiter.CancelWait();
            _selectionWaiter = null;
            Dev.DialoguePresentation.ClearChoices();
            _gameStateManager.TryGoToState(_selectionReturnState);
            return true;
        }


        public void Execute(ResetGameStateCommand command)
        {
            _selectionWaiter?.CancelWait();
            _selectionWaiter = null;
            _lastSelectedButtonIndex = 0;
            Dev.DialoguePresentation.ClearChoices();
            _lastAvatarCommand = null;
            _totalTimeUsedBeforeSkippingTheLastDialogue = 0f;
            _dialogueRenderQueue.Clear();
            _reactionTimer?.Reset();

            if (!_flashingAnimationCts.IsCancellationRequested)
            {
                _flashingAnimationCts.Cancel();
                _flashingAnimationCts = new CancellationTokenSource();
            }

            ResetUI();
        }
    }
}
