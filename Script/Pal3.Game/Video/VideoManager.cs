// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Video
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Command;
    using Command.Extensions;
    using Core.Command;
    using Core.Command.SceCommands;
    using Core.Utilities;
    using Data;
    using Engine.Logging;
    using Input;
    using Script.Waiter;
    using State;
    using UnityEngine.InputSystem;
    using UnrealSharp.CoreUObject;
    using UnrealSharp.RuntimeBinkVideo;

    public sealed class VideoManager : IDisposable,
        ICommandExecutor<PlayVideoCommand>,
        ICommandExecutor<ResetGameStateCommand>,
        ICommandExecutor<GameStateChangedNotification>
    {
        private sealed class Preparation(Task<(string Path, bool NeedsConversion)> source)
        {
            public readonly Task<(string Path, bool NeedsConversion)> Source = source;
            public URuntimeBinkConversion? Conversion;
            public string? PlaybackPath;
            public string? Error;
        }

        private readonly GameResourceProvider _resourceProvider;
        private readonly GameStateManager _gameStateManager;
        private readonly PlayerInputActions _inputActions;
        private readonly Dictionary<string, Preparation> _preparations = new();
        private URuntimeBinkVideoWidget? _video;
        private Preparation? _activePreparation;
        private WaitUntilCanceled? _waiter;
        private bool _disposed;
        public bool IsPreparing => _waiter != null && _video == null;
        public string PreparationPhase => _activePreparation?.Conversion == null ? "正在读取过场动画…" : "正在转换过场动画…";
        public float PreparationProgress => _activePreparation?.Conversion is { } conversion && conversion.IsValid()
            ? conversion.QueryProgress() : -1f;

        public VideoManager(GameResourceProvider resourceProvider, GameStateManager gameStateManager,
            PlayerInputActions inputActions)
        {
            _resourceProvider = Requires.IsNotNull(resourceProvider, nameof(resourceProvider));
            _gameStateManager = Requires.IsNotNull(gameStateManager, nameof(gameStateManager));
            _inputActions = Requires.IsNotNull(inputActions, nameof(inputActions));
            _inputActions.VideoPlaying.SkipVideo.performed += SkipVideoPerformed;
            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);
            _inputActions.VideoPlaying.SkipVideo.performed -= SkipVideoPerformed;
            StopPlayback(restoreState: false);

            // Native jobs own their staging files once started. A task still staging on
            // world teardown has no coroutine left to hand its file to a native job.
            foreach (Preparation preparation in _preparations.Values)
            {
                if (preparation.Conversion is { } conversion)
                {
                    if (conversion.IsValid()) conversion.ReleaseHandle();
                }
                else
                {
                    _ = preparation.Source.ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully && task.Result.NeedsConversion) DeleteStagedFile(task.Result.Path);
                        _ = task.Exception;
                    }, TaskScheduler.Default);
                }
            }
            _preparations.Clear();
        }

        private void SkipVideoPerformed(InputAction.CallbackContext _) => Skip();
        public void Skip() => StopPlayback(restoreState: true);
        public void Execute(ResetGameStateCommand _) => Skip();
        public void Execute(GameStateChangedNotification notification)
        {
            if (notification.PreviousState == GameState.VideoPlaying && notification.NewState != GameState.VideoPlaying)
                StopPlayback(restoreState: false);
        }

        private IEnumerator WaitForPlayback(URuntimeBinkVideoWidget video)
        {
            while (_video == video && video.IsValid() && video.PlaybackActive)
                yield return null;

            if (_video != video) yield break;
            bool failed = !video.IsValid() || video.PlaybackFailed;
            StopPlayback(restoreState: true);
            if (failed) ShowSkippedNote("过场动画播放失败，已跳过");
        }

        private void StopPlayback(bool restoreState)
        {
            if (_waiter == null) return;
            if (_video.IsValid()) _video!.CloseVideo();
            _video = null;
            _activePreparation = null;
            _waiter.CancelWait();
            _waiter = null;
            if (restoreState && _gameStateManager.GetCurrentState() == GameState.VideoPlaying)
            {
                _gameStateManager.GoToPreviousState();
                // A running script can veto returning to Gameplay. Resume it in Cutscene.
                if (_gameStateManager.GetCurrentState() == GameState.VideoPlaying)
                    _gameStateManager.TryGoToState(GameState.Cutscene);
            }
        }

        public void Execute(PlayVideoCommand command)
        {
            StopPlayback(restoreState: true);
            string path;
            try
            {
                path = _resourceProvider.GetVideoCachePath(command.Name);
            }
            catch (ArgumentException ex)
            {
                EngineLogger.LogWarning(ex.Message);
                ShowSkippedNote("无法打开过场动画，已跳过");
                return;
            }

            _gameStateManager.TryGoToState(GameState.VideoPlaying);
            _waiter = new WaitUntilCanceled();
            Pal3.Instance.Execute(new ScriptRunnerAddWaiterRequest(_waiter));
            Pal3.Instance.StartCoroutine(PrepareAndPlay(command.Name, path, _waiter));
        }

        private IEnumerator PrepareAndPlay(string name, string path, WaitUntilCanceled waiter)
        {
            if (_waiter != waiter) yield break;
            var cache = new FileInfo(path);
            bool usesBinkCache = true;
            if (!cache.Exists || cache.Length == 0)
            {
                if (!_preparations.TryGetValue(path, out Preparation? preparation))
                {
                    string cachePath = path;
                    bool stageReadyVideo = UnrealSharp.Engine.UGameplayStatics.PlatformName == "IOS";
                    preparation = new Preparation(Task.Run(() => _resourceProvider.PrepareVideoSource(name, cachePath, stageReadyVideo)));
                    _preparations.Add(cachePath, preparation);
                    Pal3.Instance.StartCoroutine(PrepareVideo(cachePath, preparation));
                }

                _activePreparation = preparation;
                while (_waiter == waiter && preparation.PlaybackPath == null && preparation.Error == null) yield return null;
                if (_waiter != waiter) yield break;
                _activePreparation = null;
                if (preparation.Error != null)
                {
                    Fail(waiter, preparation.Error);
                    yield break;
                }

                usesBinkCache = preparation.PlaybackPath == path;
                path = preparation.PlaybackPath!;
            }

            if (_waiter != waiter) yield break;
            // The 640x480 opening movie has 60-pixel black bands above and below the image.
            bool isOpening = name.Equals("pal3op", StringComparison.OrdinalIgnoreCase);
            FVector2D verticalUvRange = isOpening && usesBinkCache ? new(0.125, 0.875) : new(0, 1);
            _video = URuntimeBinkVideoWidget.OpenVideo(path, 77, verticalUvRange, isOpening);
            if (_video == null)
            {
                Fail(waiter, $"UE could not open {path}");
                yield break;
            }
            yield return WaitForPlayback(_video);
        }

        private IEnumerator PrepareVideo(string cachePath, Preparation preparation)
        {
            while (!_disposed && !preparation.Source.IsCompleted) yield return null;
            if (_disposed) yield break;

            if (!preparation.Source.IsCompletedSuccessfully)
            {
                preparation.Error = preparation.Source.Exception?.GetBaseException().Message ?? $"Could not prepare {cachePath}";
            }
            else
            {
                var (source, needsConversion) = preparation.Source.Result;
                if (!needsConversion)
                {
                    preparation.PlaybackPath = source;
                }
                else
                {
                    var conversion = URuntimeBinkConversion.ConvertStagedFile(source, cachePath);
                    if (conversion == null)
                    {
                        DeleteStagedFile(source);
                        preparation.Error = $"Could not start Bink conversion for {cachePath}";
                    }
                    else
                    {
                        preparation.Conversion = conversion;
                        int result = -1;
                        while (!_disposed && conversion.IsValid() && (result = conversion.QueryResult()) == 0) yield return null;
                        if (_disposed) yield break;
                        preparation.Conversion = null;
                        if (result > 0) preparation.PlaybackPath = cachePath;
                        else preparation.Error = $"Bink conversion failed for {cachePath}";
                    }
                }
            }
            _preparations.Remove(cachePath);
        }

        private static void DeleteStagedFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void Fail(WaitUntilCanceled waiter, string reason)
        {
            EngineLogger.LogWarning(reason);
            if (_waiter != waiter) return;
            StopPlayback(restoreState: true);
            ShowSkippedNote("无法播放过场动画，已跳过");
        }

        private static void ShowSkippedNote(string message)
            => Pal3.Instance.Execute(new UIDisplayNoteCommand(message));
    }
}
