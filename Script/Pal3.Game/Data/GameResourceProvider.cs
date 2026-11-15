// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Data
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Command;
    using Command.Extensions;
    using Constants;
    using Core.Command;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Core.DataReader;
    using Core.DataReader.Cpk;
    using Core.DataReader.Gdb;
    using Core.DataReader.Ini;
    using Core.DataReader.Mv3;
    using Core.FileSystem;
    using Core.Utilities;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.DataLoader;
    using Engine.Extensions;
    using Engine.Logging;
    using Engine.Services;
    using Engine.Utilities;
    using Rendering.Material;
    using Settings;
    using UnityEngine;
    using WrapMode = Engine.Core.Abstraction.WrapMode;

    /// <summary>
    /// Single resource provider for accessing game data.
    /// Also manages the lifecycle of the resource it provides.
    /// </summary>
    public sealed class GameResourceProvider : IDisposable,
        ICommandExecutor<SettingChangedNotification>
    {
        private const string CACHE_FOLDER_NAME = "CacheData";
        private const string MV3_ACTOR_CONFIG_HEADER = ";#MV3#";

        private const char DIR_SEPARATOR = CpkConstants.DirectorySeparatorChar;

        private readonly ICpkFileSystem _fileSystem;
        private readonly ITextureLoaderFactory _textureLoaderFactory;
        private readonly ITextureFactory _textureFactory;
        private readonly IMaterialManager _unlitMaterialManager;
        private readonly IMaterialManager _litMaterialManager;
        private readonly GameSettings _gameSettings;

        private readonly GdbFile _gameDatabase;

        private ITextureCache _textureCache;

        private readonly Dictionary<string, ISprite> _spriteCache = new ();
        private readonly Dictionary<string, AudioClip> _audioClipCache = new ();
        private readonly Dictionary<int, UnityEngine.Object> _vfxEffectPrefabCache = new ();

        private readonly Dictionary<Type, Dictionary<string, object>> _gameResourceFileCache = new ();

        private readonly int _codepage;

        public GameResourceProvider(ICpkFileSystem fileSystem,
            ITextureLoaderFactory textureLoaderFactory,
            ITextureFactory textureFactory,
            IMaterialManager unlitMaterialManager,
            IMaterialManager litMaterialManager,
            GameSettings gameSettings)
        {
            _fileSystem = Requires.IsNotNull(fileSystem, nameof(fileSystem));
            _textureLoaderFactory = Requires.IsNotNull(textureLoaderFactory, nameof(textureLoaderFactory));
            _textureFactory = Requires.IsNotNull(textureFactory, nameof(textureFactory));
            _unlitMaterialManager = Requires.IsNotNull(unlitMaterialManager, nameof(unlitMaterialManager));
            _litMaterialManager = litMaterialManager; // Lit materials are not required
            _gameSettings = Requires.IsNotNull(gameSettings, nameof(gameSettings));

            _codepage = _gameSettings.Language == Language.SimplifiedChinese ? 936 : 950;
            _gameDatabase = GetGameDatabaseFile(); // Initialize game database file

            CommandExecutorRegistry<ICommand>.Instance.Register(this);
        }

        private GdbFile GetGameDatabaseFile()
        {
            string gdbFilePath = FileConstants.CombatDataFolderVirtualPath + $"{GameConstants.AppName}_Softstar.gdb";
            return GetGameResourceFile<GdbFile>(gdbFilePath);
        }

        public void Dispose()
        {
            CommandExecutorRegistry<ICommand>.Instance.UnRegister(this);

            _textureCache?.DisposeAll();

            foreach (ISprite sprite in _spriteCache.Values)
            {
                sprite.Destroy();
            }
            _spriteCache.Clear();

            foreach (AudioClip audioClip in _audioClipCache.Values)
            {
                audioClip.Destroy();
            }
            _audioClipCache.Clear();

            _vfxEffectPrefabCache.Clear();

            _unlitMaterialManager?.DeallocateMaterialPool();
            _litMaterialManager?.DeallocateMaterialPool();

            _gameResourceFileCache.Clear();
        }

        public void UseTextureCache(ITextureCache textureCache)
        {
            _textureCache = textureCache;
        }

        public bool FileExists(string path)
        {
            return _fileSystem.FileExists(path);
        }

        // No need to deallocate the shadow texture/sprite since it is been used almost every where.
        private static ISprite _shadowSprite;

        /// <summary>
        /// Drops the cached shadow sprite (PIE session reset): it wraps a UTexture2D
        /// created in the previous world and is garbage-collected with it.
        /// </summary>
        public static void ResetStaticCaches()
        {
            _shadowSprite = null;
        }

        public ISprite GetShadowSprite()
        {
            if (_shadowSprite != null) return _shadowSprite;

            ITexture2D shadowTexture = new UnityTexture2D(Resources.Load<Texture2D>("Textures/shadow"));
            _shadowSprite = shadowTexture.CreateSprite(0, 0,
                shadowTexture.Width, shadowTexture.Height,
                0.5f, 0.5f);

            return _shadowSprite;
        }

        public IMaterialManager GetMaterialManager()
        {
            return !_gameSettings.IsOpenSourceVersion && _gameSettings.IsRealtimeLightingAndShadowsEnabled ?
                _litMaterialManager :
                _unlitMaterialManager;
        }

        public ITextureResourceProvider CreateTextureResourceProvider(string relativeDirectoryPath, bool useCache = true)
        {
            return (_textureCache != null && useCache) ?
                new TextureProvider(_fileSystem, _textureLoaderFactory, _textureFactory, relativeDirectoryPath, _textureCache) :
                new TextureProvider(_fileSystem, _textureLoaderFactory, _textureFactory, relativeDirectoryPath);
        }

        public T GetGameResourceFile<T>(string fileVirtualPath, bool useCache = true, byte[] data = null)
        {
            fileVirtualPath = fileVirtualPath.ToLower();

            if (useCache &&
                _gameResourceFileCache.ContainsKey(typeof(T)) &&
                _gameResourceFileCache[typeof(T)].ContainsKey(fileVirtualPath))
            {
                return (T)_gameResourceFileCache[typeof(T)][fileVirtualPath];
            }

            T file = ServiceLocator.Instance.Get<IFileReader<T>>().Read(
                data ?? _fileSystem.ReadAllBytes(fileVirtualPath), _codepage);

            if (useCache)
            {
                // initialize cache for this type of file if not exists
                if (!_gameResourceFileCache.ContainsKey(typeof(T)))
                {
                    _gameResourceFileCache[typeof(T)] = new Dictionary<string, object>();
                }
                _gameResourceFileCache[typeof(T)][fileVirtualPath] = file;
            }

            return file;
        }

        public IDictionary<int, GameItemInfo> GetGameItemInfos()
        {
            return _gameDatabase.GameItemInfos;
        }

        public IDictionary<int, CombatActorInfo> GetCombatActorInfos()
        {
            return _gameDatabase.CombatActorInfos;
        }

        public IDictionary<int, SkillInfo> GetSkillInfos()
        {
            return _gameDatabase.SkillInfos;
        }

        public IDictionary<int, ComboSkillInfo> GetComboSkillInfos()
        {
            return _gameDatabase.ComboSkillInfos;
        }

        /// <summary>
        /// Get music file path in cache folder.
        /// </summary>
        /// <param name="musicFileVirtualPath">music file virtual path</param>
        /// <returns>Music file path in cache folder</returns>
        public string GetMusicFilePathInCacheFolder(string musicFileVirtualPath)
        {
            return Application.persistentDataPath
                   + Path.DirectorySeparatorChar
                   + CACHE_FOLDER_NAME
                   + Path.DirectorySeparatorChar
                   + musicFileVirtualPath
                       .Replace(CpkConstants.DirectorySeparatorChar, Path.DirectorySeparatorChar)
                       .Replace(CpkConstants.FileExtension, string.Empty);
        }

        /// <summary>
        /// Since unity cannot directly load audio clip (mp3 in this case) from memory,
        /// we need to first extract the audio clip from cpk archive, write it to a cached folder
        /// and then use this cached mp3 file path for Unity to consume (using UnityWebRequest).
        /// </summary>
        /// <param name="musicFileVirtualPath">music file virtual path</param>
        /// <param name="musicFileCachePath">music file path in cache folder</param>
        public IEnumerator ExtractAndMoveMp3FileToCacheFolderAsync(string musicFileVirtualPath, string musicFileCachePath)
        {
            if (File.Exists(musicFileCachePath)) yield break;

            EngineLogger.Log($"Writing MP3 file to App's persistent folder: {musicFileVirtualPath}");
            Thread dataMovementThread = new(() =>
            {
                try
                {
                    new DirectoryInfo(Path.GetDirectoryName(musicFileCachePath) ?? string.Empty).Create();
                    File.WriteAllBytes(musicFileCachePath, _fileSystem.ReadAllBytes(musicFileVirtualPath));
                }
                catch (Exception)
                {
                    // ignore
                }
            })
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Normal
            };
            dataMovementThread.Start();

            while (dataMovementThread.IsAlive)
            {
                yield return null;
            }
        }

        public string GetSfxFilePath(string sfxName)
        {
            #if PAL3
            string sfxFileName = (sfxName + ".wav").ToLower();
            #elif PAL3A
            string sfxFileName = (sfxName + ".wav").ToUpper();
            #endif

            string sfxFileRelativePath = $"{FileConstants.SfxFolderName}{Path.DirectorySeparatorChar}" + sfxFileName;

            string rootPath = _fileSystem.GetRootPath();
            string sfxFilePath = $"{rootPath}{sfxFileRelativePath}";

            return sfxFilePath;
        }

        public IEnumerator LoadAudioClipAsync(string filePath,
            AudioType audioType,
            bool streamAudio,
            Action<AudioClip> onLoaded)
        {
            string cacheKey = filePath.ToLower();

            if (_audioClipCache.ContainsKey(cacheKey) &&
                _audioClipCache[cacheKey] != null)
            {
                onLoaded?.Invoke(_audioClipCache[cacheKey]);
                yield break;
            }

            // Read the compressed bytes straight out of the CPK archives and hand
            // them to the UE audio helper, which decodes WAV/MPEG at runtime.
            AudioClip audioClip = null;
            try
            {
                // SFX are loose files under the game folder; music is extracted from
                // music.cpk into the cache folder first. Both are physical paths.
                byte[] data = File.Exists(filePath)
                    ? File.ReadAllBytes(filePath)
                    : _fileSystem.ReadAllBytes(filePath);
                if (data is { Length: > 16 })
                {
                    audioClip = AudioClip.FromCompressedData(filePath, data, (int)audioType);
                }
            }
            catch (Exception e)
            {
                EngineLogger.LogWarning($"[Audio] failed to read {filePath}: {e.Message}");
            }

            if (audioClip != null)
            {
                _audioClipCache[cacheKey] = audioClip;
            }
            else
            {
                EngineLogger.LogWarning($"[Audio] clip not found: {filePath}");
            }

            onLoaded?.Invoke(audioClip);
            yield break;
        }

        public ISprite? GetLogoSprite() => GetLogoSprite(useCache: true);

        public ISprite? GetLogoSprite(bool useCache)
        {
            #if PAL3
            const string mainLogoTextureFileName = "11.tga";
            Rect mainLogoSpriteRect = new Rect(0, 600, 375, 145);
            #elif PAL3A
            const string mainLogoTextureFileName = "12.tga";
            Rect mainLogoSpriteRect = new Rect(0, 770, 500, 253);
            #endif

            string logoPath = FileConstants.UILibFolderVirtualPath + mainLogoTextureFileName;
            ITexture2D? logoTexture;
            try
            {
                logoTexture = CreateTextureResourceProvider(FileConstants.UILibFolderVirtualPath, useCache)
                    .GetTexture(mainLogoTextureFileName);
            }
            catch (Exception error)
            {
                EngineLogger.LogWarning($"Could not read PAL3 logo {logoPath}: {error.Message}");
                return null;
            }

            if (logoTexture == null) return null;
            if (mainLogoSpriteRect.x + mainLogoSpriteRect.width > logoTexture.Width ||
                mainLogoSpriteRect.y + mainLogoSpriteRect.height > logoTexture.Height)
            {
                EngineLogger.LogWarning($"PAL3 logo crop exceeds atlas bounds: {logoPath} ({logoTexture.Width}x{logoTexture.Height})");
                if (!useCache || _textureCache == null) logoTexture.Destroy();
                return null;
            }

            return logoTexture.CreateSprite(mainLogoSpriteRect.x, mainLogoSpriteRect.y,
                mainLogoSpriteRect.width, mainLogoSpriteRect.height,
                0.5f, 0.5f);
        }

        private ITexture2D GetActorAvatarTexture(string actorName, string avatarTextureName)
        {
            string roleAvatarTextureRelativePath = FileConstants.GetActorFolderVirtualPath(actorName);
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(roleAvatarTextureRelativePath);
            return textureProvider.GetTexture($"{avatarTextureName}.tga");
        }

        public ISprite GetActorAvatarSprite(string actorName, string avatarName)
        {
            string cacheKey = $"ActorAvatar_{actorName}_{avatarName}";

            if (_spriteCache.TryGetValue(cacheKey, out ISprite sprite))
            {
                if (sprite is { Texture: { IsNativeObjectDisposed: false } }) return sprite;
            }

            ITexture2D texture = GetActorAvatarTexture(
                actorName, avatarName);

            if (texture == null) return null;

            ISprite avatarSprite = texture.CreateSprite(
                // Cut 2f to hide artifacts near edges for some of the avatar textures
                2f, 0f,
                texture.Width - 2f, texture.Height,
                0.5f, 0f);

            _spriteCache[cacheKey] = avatarSprite;
            return avatarSprite;
        }

        private ITexture2D GetEmojiSpriteSheetTexture(ActorEmojiType emojiType)
        {
            string emojiSpriteSheetRelativePath = FileConstants.EmojiSpriteSheetFolderVirtualPath;
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(emojiSpriteSheetRelativePath);
            return textureProvider.GetTexture($"EM_{(int)emojiType:00}.tga");
        }

        public ITexture2D GetCaptionTexture(string name)
        {
            string captionTextureRelativePath = FileConstants.CaptionFolderVirtualPath;
            // No need to cache caption texture since it is a one time thing
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(captionTextureRelativePath, useCache: false);
            return textureProvider.GetTexture($"{name}.tga");
        }

        public ITexture2D[] GetSkyBoxTextures(int skyBoxId)
        {
            string relativeFilePath = string.Format(FileConstants.SkyBoxTexturePathFormat.First(), skyBoxId);

            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(
                CoreUtility.GetDirectoryName(relativeFilePath, DIR_SEPARATOR));

            ITexture2D[] textures = new ITexture2D[FileConstants.SkyBoxTexturePathFormat.Length];
            for (int i = 0; i < FileConstants.SkyBoxTexturePathFormat.Length; i++)
            {
                string textureNameFormat = CoreUtility.GetFileName(
                    string.Format(FileConstants.SkyBoxTexturePathFormat[i], skyBoxId), DIR_SEPARATOR);
                ITexture2D texture = textureProvider.GetTexture(string.Format(textureNameFormat, i));
                // Set wrap mode to clamp to remove "edges" between sides
                texture.SetWrapMode(WrapMode.Clamp);
                textures[i] = texture;
            }

            return textures;
        }

        public ITexture2D GetEffectTexture(string name, out bool hasAlphaChannel)
        {
            string effectFolderRelativePath = FileConstants.EffectFolderVirtualPath;
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(effectFolderRelativePath);
            return textureProvider.GetTexture(name, out hasAlphaChannel);
        }

        public ISprite[] GetEmojiSprites(ActorEmojiType emojiType)
        {
            (int Width, int Height, int Frames) textureInfo = ActorEmojiConstants.TextureInfo[emojiType];
            ITexture2D spriteSheetTexture = GetEmojiSpriteSheetTexture(emojiType);

            float widthIndex = 0f;
            ISprite[] sprites = new ISprite[textureInfo.Frames];

            for (int i = 0; i < textureInfo.Frames; i++)
            {
                string cacheKey = $"EmojiSprite_{emojiType}_{i}";

                if (_spriteCache.TryGetValue(cacheKey, out ISprite sprite))
                {
                    if (sprite is { Texture: { IsNativeObjectDisposed: false } })
                    {
                        sprites[i] = sprite;
                        continue;
                    }
                }

                ISprite emojiSprite = spriteSheetTexture.CreateSprite(widthIndex, 0f,
                    textureInfo.Width, textureInfo.Height,
                    0.5f, 0f);

                _spriteCache[cacheKey] = emojiSprite;
                sprites[i] = emojiSprite;

                widthIndex += textureInfo.Width;
            }

            return sprites;
        }

        public ISprite[] GetJumpIndicatorSprites()
        {
            string relativePath = FileConstants.UISceneFolderVirtualPath;
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(relativePath);

            ISprite[] sprites = new ISprite[4];

            for (int i = 0; i < 4; i++)
            {
                string cacheKey = $"JumpIndicatorSprite_{i}";

                if (_spriteCache.TryGetValue(cacheKey, out ISprite sprite))
                {
                    if (sprite is { Texture: { IsNativeObjectDisposed: false } })
                    {
                        sprites[i] = sprite;
                        continue;
                    }
                }

                ITexture2D texture = textureProvider.GetTexture($"tiao{i}.tga");
                ISprite jumpIndicatorSprite = texture.CreateSprite(0f, 0f,
                    40f, 32f,
                    0.5f, 0f);
                _spriteCache[cacheKey] = jumpIndicatorSprite;
                sprites[i] = jumpIndicatorSprite;
            }

            return sprites;
        }

        public ActorActionConfig GetActorActionConfig(string actorName, string configFileName)
        {
            string actorConfigFilePath = FileConstants.GetActorFolderVirtualPath(actorName) + configFileName;

            actorConfigFilePath = actorConfigFilePath.ToLower();

            if (!FileExists(actorConfigFilePath))
            {
                return null;
            }

            byte[] configData = _fileSystem.ReadAllBytes(actorConfigFilePath);
            string configHeaderStr = Encoding.ASCII.GetString(configData[..MV3_ACTOR_CONFIG_HEADER.Length]);

            if (string.Equals(MV3_ACTOR_CONFIG_HEADER, configHeaderStr))
            {
                return GetGameResourceFile<Mv3ActionConfig>(actorConfigFilePath, data: configData);
            }
            else // MOV actor config
            {
                return GetGameResourceFile<MovActionConfig>(actorConfigFilePath, data: configData);
            }
        }

        public string GetVideoCachePath(string videoName)
        {
            if (string.IsNullOrEmpty(videoName) ||
                !videoName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new ArgumentException($"Invalid video name: {videoName}", nameof(videoName));

            return Path.Combine(Application.persistentDataPath, "Movies", videoName.ToLowerInvariant() + ".mp4");
        }

        public (string Path, bool NeedsConversion) PrepareVideoSource(string videoName, string cachePath, bool stageReadyVideo)
        {
            string movieFolder = Path.Combine(_fileSystem.GetRootPath(), FileConstants.MovieFolderName);
            if (Directory.Exists(movieFolder))
            {
                string? readyVideo = Directory.EnumerateFiles(movieFolder, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals(videoName, StringComparison.OrdinalIgnoreCase) &&
                        Path.GetExtension(file).ToLowerInvariant() is ".mp4" or ".mov" or ".webm");
                if (readyVideo != null)
                {
                    // AV Foundation opens the local copy through UE's platform file layer.
                    return (stageReadyVideo ? StageReadyVideo(readyVideo, cachePath) : readyVideo, false);
                }
            }

            return (StageOriginalVideo(videoName, cachePath, movieFolder), true);
        }

        private static readonly object ReadyVideoCacheLock = new();

        private static string StageReadyVideo(string source, string cachePath)
        {
            string directory = Path.Combine(Path.GetDirectoryName(cachePath)!, "Imported");
            // Keep the original container extension and distinguish it from converted Bink video.
            string destination = Path.Combine(directory, Path.GetFileNameWithoutExtension(cachePath) + Path.GetExtension(source));
            string stampPath = destination + ".source";

            lock (ReadyVideoCacheLock)
            {
                var sourceFile = new FileInfo(source);
                long length = sourceFile.Length;
                DateTime lastWrite = sourceFile.LastWriteTimeUtc;
                string stamp = FormattableString.Invariant($"{length}:{lastWrite.Ticks}:{sourceFile.FullName}");
                var cachedFile = new FileInfo(destination);
                if (cachedFile.Exists && cachedFile.Length == length &&
                    File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
                {
                    return destination;
                }

                Directory.CreateDirectory(directory);
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(sourceFile.FullName, temporary);
                    sourceFile.Refresh();
                    if (sourceFile.Length != length || sourceFile.LastWriteTimeUtc != lastWrite ||
                        new FileInfo(temporary).Length != length)
                    {
                        throw new IOException($"PAL3 video changed while copying: {source}");
                    }

                    // An interrupted replacement must leave a cache miss on the next launch.
                    File.Delete(stampPath);
                    File.Move(temporary, destination, overwrite: true);
                    try
                    {
                        File.WriteAllText(stampPath, stamp);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        EngineLogger.LogWarning($"Could not write PAL3 video cache marker for {destination}: {ex.Message}");
                    }
                    return destination;
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
        }

        private string StageOriginalVideo(string videoName, string cachePath, string movieFolder)
        {
            string sourceName = videoName + ".bik";
            string staged = cachePath + "." + Guid.NewGuid().ToString("N") + ".staged.bik";
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            try
            {
                string? loose = Directory.Exists(movieFolder)
                    ? Directory.EnumerateFiles(movieFolder).FirstOrDefault(file =>
                        Path.GetFileName(file).Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (loose != null)
                {
                    File.Copy(loose, staged);
                    return staged;
                }

                var crc = new Crc32Hash();
                crc.Init();
                // Keep movie indexes separate from the mounted scene archives on the game thread.
                var movies = new CpkFileSystem(_fileSystem.GetRootPath(), crc, _codepage);
                foreach (string cpk in FileConstants.MovieCpkFileNames)
                {
                    string relative = FileConstants.GetMovieCpkFileRelativePath(cpk);
                    if (File.Exists(Path.Combine(_fileSystem.GetRootPath(), relative)))
                        movies.Mount(relative, _codepage);
                }
                string? entry = movies.Search(sourceName).FirstOrDefault(path =>
                    path.Split(CpkConstants.DirectorySeparatorChar)[^1]
                        .Equals(sourceName, StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new FileNotFoundException($"Original PAL3 video {sourceName} was not found.");

                using FileStream output = new(staged, FileMode.CreateNew, FileAccess.Write);
                movies.CopyTo(entry, output);
                return staged;
            }
            catch
            {
                File.Delete(staged);
                throw;
            }
        }

        public (ITexture2D texture, bool hasAlphaChannel)[] GetEffectTextures(GraphicsEffectType effect, string texturePathFormat)
        {
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(
                CoreUtility.GetDirectoryName(texturePathFormat, DIR_SEPARATOR));

            if (effect == GraphicsEffectType.Fire)
            {
                int numberOfFrames = EffectConstants.AnimatedFireEffectFrameCount;
                (ITexture2D texture, bool hasAlphaChannel)[] textures = new (ITexture2D texture, bool hasAlphaChannel)[numberOfFrames];
                for (int i = 0; i < numberOfFrames; i++)
                {
                    string textureNameFormat = CoreUtility.GetFileName(texturePathFormat, DIR_SEPARATOR);
                    ITexture2D texture = textureProvider.GetTexture(string.Format(textureNameFormat, i + 1), out var hasAlphaChannel);
                    textures[i] = (texture, hasAlphaChannel);
                }

                return textures;
            }

            return Array.Empty<(ITexture2D, bool)>();
        }

        private string GetVfxPrefabPath(int effectGroupId)
        {
            return $"Prefabs/VFX/{GameConstants.AppName}/{effectGroupId}";
        }

        public object GetVfxEffectPrefab(int effectGroupId)
        {
            if (_vfxEffectPrefabCache.TryGetValue(effectGroupId, out UnityEngine.Object vfxEffectPrefab))
            {
                return vfxEffectPrefab;
            }

            UnityEngine.Object vfxPrefab = Resources.Load(GetVfxPrefabPath(effectGroupId));

            if (vfxPrefab == null)
            {
                EngineLogger.LogWarning($"VFX prefab not found: {effectGroupId}");
            }

            _vfxEffectPrefabCache[effectGroupId] = vfxPrefab;
            return vfxPrefab;
        }

        public IEnumerator PreLoadVfxEffectAsync(int effectGroupId)
        {
            if (_vfxEffectPrefabCache.ContainsKey(effectGroupId))
            {
                yield break;
            }

            ResourceRequest request = Resources.LoadAsync(GetVfxPrefabPath(effectGroupId));

            while (!request.isDone)
            {
                yield return request;
            }

            if (request.asset == null)
            {
                EngineLogger.LogWarning($"VFX prefab not found: {effectGroupId}");
            }
            else
            {
                EngineLogger.Log($"VFX prefab preloaded: {effectGroupId}");
            }

            _vfxEffectPrefabCache[effectGroupId] = request.asset;
        }

        public ITexture2D GetCursorTexture()
        {
            string cursorSpriteRelativePath = FileConstants.CursorSpriteFolderVirtualPath;
            ITextureResourceProvider textureProvider = CreateTextureResourceProvider(cursorSpriteRelativePath);
            ITexture2D cursorTexture = textureProvider.GetTexture($"jt.tga");
            return cursorTexture;
        }

        internal void ClearSceneCaches()
        {
            _textureCache?.DisposeAll();
            _spriteCache.Clear();
            // The music player may still own a clip across the transition.
            _audioClipCache.Clear();
            _gameResourceFileCache.Clear();
            _vfxEffectPrefabCache.Clear();
        }

        public void Execute(SettingChangedNotification command)
        {
            // Need to deallocate and reallocate material pool when switching between lit and unlit materials
            if (command.SettingName == nameof(_gameSettings.IsRealtimeLightingAndShadowsEnabled))
            {
                if (_gameSettings.IsRealtimeLightingAndShadowsEnabled)
                {
                    _unlitMaterialManager?.DeallocateMaterialPool();
                    _litMaterialManager?.AllocateMaterialPool();
                }
                else
                {
                    _litMaterialManager?.DeallocateMaterialPool();
                    _unlitMaterialManager?.AllocateMaterialPool();
                }
            }
        }
    }
}
