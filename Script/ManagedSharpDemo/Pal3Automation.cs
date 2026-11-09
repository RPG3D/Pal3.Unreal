using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Engine.Services;
using Pal3.Core.Contract.Constants;
using Pal3.Core.Command.SceCommands;
using Pal3.Game;
using Pal3.Game.Actor.Controllers;
using Pal3.Game.Camera;
using Pal3.Game.Dev;
using Pal3.Game.GamePlay;
using Pal3.Game.GameSystems.Caption;
using Pal3.Game.GameSystems.Dialogue;
using Pal3.Game.GameSystems.Team;
using Pal3.Game.Scene;
using Pal3.Game.Script;
using Pal3.Game.State;
using Debug = UnityEngine.Debug;

namespace ManagedSharpDemo
{
    internal sealed class Pal3Automation(Pal3TouchStick touchStick)
    {
        private sealed record Request(string Command, string? Argument, DateTimeOffset DeadlineUtc);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private string? _sessionDirectory;
        private long _lastPoll;
        private double _cameraUntil;
        private float _cameraAxis;

        public void Start()
        {
            string? directory = Environment.GetEnvironmentVariable("PAL3_AUTOMATION_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;

            try
            {
                string session = Guid.NewGuid().ToString("N");
                string root = Path.GetFullPath(directory);
                string sessionDirectory = Path.Combine(root, session);
                Directory.CreateDirectory(sessionDirectory);
                WriteJson(Path.Combine(root, "session.json"), new { session, pid = Environment.ProcessId });
                _sessionDirectory = sessionDirectory;
            }
            catch (Exception e)
            {
                Engine.Diagnostics.DebugOverlay.Error("[PAL3 automation] " + e.Message);
            }
        }

        public void Stop()
        {
            if (_sessionDirectory == null) return;
            try
            {
                WriteJson(Path.Combine(_sessionDirectory, "stopped.json"), new { stopped = true });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Engine.Diagnostics.DebugOverlay.Error("[PAL3 automation] " + e.Message);
            }
            finally
            {
                _sessionDirectory = null;
            }
        }

        // World state and actions are accessed on the UE game thread.
        public void Poll(Action afterTransition)
        {
            if (_sessionDirectory == null || Stopwatch.GetElapsedTime(_lastPoll).TotalMilliseconds < 200) return;
            _lastPoll = Stopwatch.GetTimestamp();

            try
            {
                string? path = Directory.EnumerateFiles(_sessionDirectory, "*.request.json").FirstOrDefault();
                if (path == null) return;
                string id = Path.GetFileName(path).Split('.')[0];
                if (Path.GetFileName(path) != id + ".request.json" || !Guid.TryParseExact(id, "N", out _)) return;

                // Claim the request before dispatch so a response write failure cannot repeat it.
                string payload = File.ReadAllText(path);
                File.Delete(path);
                string response = Path.Combine(_sessionDirectory, id + ".response.json");
                try
                {
                    var request = JsonSerializer.Deserialize<Request>(payload, JsonOptions)
                        ?? throw new FormatException("Empty PAL3 request.");
                    if (request.DeadlineUtc <= DateTimeOffset.UtcNow) throw new TimeoutException("Request expired before dispatch.");
                    object result = Execute(request, afterTransition);
                    WriteJson(response, new { ok = true, result });
                }
                catch (Exception e)
                {
                    string error = e.GetBaseException().Message;
                    Engine.Diagnostics.DebugOverlay.Error("[PAL3 automation] " + e);
                    WriteJson(response, new { ok = false, error });
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Engine.Diagnostics.DebugOverlay.Error("[PAL3 automation] " + e.Message);
            }
        }

        private object Execute(Request request, Action afterTransition)
        {
            if (Pal3Boot.IsGameReady && ServiceLocator.Instance.Get<SceneManager>().IsLoading &&
                request.Command is not ("status" or "list" or "slots"))
            {
                throw new InvalidOperationException("A PAL3 scene is already loading.");
            }
            switch (request.Command)
            {
                case "status": return Snapshot();
                case "retry-boot":
                    Pal3Boot.RunFullBoot();
                    return Snapshot();
                case "pose": return SampleActorPose(request.Argument);
                case "slots":
                    if (!Pal3Boot.IsGameReady) throw new InvalidOperationException("PAL3 is not ready.");
                    var saves = ServiceLocator.Instance.Get<SaveManager>();
                    return Enumerable.Range(0, SaveManager.ManualSlotCount + 1).Select(slot => new
                    {
                        slot,
                        occupied = saves.SaveSlotExists(slot),
                        lastWriteTime = saves.SaveSlotExists(slot) ? saves.GetSaveSlotLastWriteTime(slot) : (DateTime?)null
                    }).ToArray();
                case "save":
                    RequirePlayerControl();
                    Pal3Boot.SaveToSlot(ParseSlot(request.Argument));
                    return Snapshot();
                case "list":
                    return new { checkpoints = Pal3StartMenu.Stories, scenes = Pal3StartMenu.Scenes.Select(scene => scene.Label) };
                case "auto":
                    if (!bool.TryParse(request.Argument, out bool auto)) throw new ArgumentException("Use true or false.");
                    Pal3Boot.AutoDialogueEnabled = auto;
                    return Snapshot();
                case "overlay":
                    if (!bool.TryParse(request.Argument, out bool visible)) throw new ArgumentException("Use true or false.");
                    Engine.Diagnostics.DebugOverlay.IsOverlayVisible = visible;
                    return Snapshot();
                case "touch-sticks":
                    if (!Pal3TouchStick.IsSupported) throw new InvalidOperationException("Touch controls are disabled.");
                    if (!bool.TryParse(request.Argument, out bool transparent)) throw new ArgumentException("Use true or false.");
                    touchStick.FullyTransparent = transparent;
                    return new { transparent = touchStick.FullyTransparent };
                case "demo":
                    JsonUiDemo.Show();
                    return new { visible = JsonUiDemo.IsVisible };
                case "advance":
                    if (!Pal3Boot.IsGameReady) throw new InvalidOperationException("PAL3 is not ready.");
                    if (ServiceLocator.Instance.Get<GameStateManager>().GetCurrentState() == GameState.VideoPlaying)
                    {
                        ServiceLocator.Instance.Get<Pal3.Game.Video.VideoManager>().Skip();
                        return Snapshot();
                    }
                    ServiceLocator.Instance.Get<DialogueManager>().RequestContinue();
                    ServiceLocator.Instance.Get<CaptionRenderer>().RequestContinue();
                    return Snapshot();
                case "video":
                    if (!Pal3Boot.IsGameReady || string.IsNullOrWhiteSpace(request.Argument))
                        throw new InvalidOperationException("Use video <clip name> after PAL3 starts.");
                    Pal3Game.Instance.Execute(new PlayVideoCommand(request.Argument));
                    return Snapshot();
                case "choose":
                    if (!Pal3Boot.IsGameReady || !int.TryParse(request.Argument, out int choice) || choice < 1 ||
                        !ServiceLocator.Instance.Get<DialogueManager>().TryChoose(choice - 1))
                    {
                        throw new ArgumentException("Choose a valid 1-based index from the pending dialogue choices.");
                    }
                    return Snapshot();
                case "move":
                    RequirePlayerControl();
                    ScheduleMovement(request.Argument);
                    return Snapshot();
                case "rotate-camera":
                    RequirePlayerControl();
                    if (!ServiceLocator.Instance.Get<CameraManager>().CanRotateByUser)
                        throw new InvalidOperationException("This PAL3 camera is locked.");
                    var (turn, duration) = ParseTimedDirection(request.Argument);
                    _cameraAxis = turn switch
                    {
                        "left" => -1f,
                        "right" => 1f,
                        _ => throw new ArgumentException("Camera direction must be left or right.")
                    };
                    _cameraUntil = GameTimeProvider.Instance.TimeSinceStartup + duration;
                    return Snapshot();
                case "move-to":
                    RequirePlayerControl();
                    MoveToTile(request.Argument);
                    return Snapshot();
                case "interact":
                    RequirePlayerControl();
                    Pal3Game.Instance.Execute(new Pal3.Game.Command.Extensions.PlayerInteractionRequest());
                    return Snapshot();
                case "scene-script":
                    RequirePlayerControl();
                    RunSceneScript(request.Argument);
                    return Snapshot();
            }

            if (!Pal3Boot.IsGameReady || (!Pal3Boot.WaitingForMenuStart &&
                ServiceLocator.Instance.Get<GameStateManager>().GetCurrentState() != GameState.Gameplay))
            {
                throw new InvalidOperationException("Start, load, checkpoint and scene require the title or gameplay state.");
            }

            switch (request.Command)
            {
                case "start":
                    RunTransition("new game", Pal3Boot.StartFromMenu);
                    break;
                case "load":
                    int slot = ParseSlot(request.Argument);
                    RunTransition($"load slot {slot}", () => Pal3Boot.LoadFromSlot(slot));
                    break;
                case "checkpoint":
                    string checkpoint = Select(Pal3StartMenu.Stories, request.Argument);
                    RunTransition(checkpoint, () => Pal3Boot.StartFromCheckpoint(checkpoint));
                    break;
                case "scene":
                    string label = Select(Pal3StartMenu.Scenes.Select(scene => scene.Label).ToArray(), request.Argument);
                    var scene = Pal3StartMenu.Scenes.Single(scene => scene.Label == label);
                    RunTransition($"{scene.City}/{scene.Scene}", () => Pal3Boot.SwitchScene(scene.City, scene.Scene, scene.X, scene.Y));
                    break;
                default:
                    throw new ArgumentException($"Unknown PAL3 automation command: {request.Command}");
            }

            afterTransition();
            return Snapshot();
        }

        private static int ParseSlot(string? argument) =>
            int.TryParse(argument, out int slot) && slot is >= 0 and <= SaveManager.ManualSlotCount
                ? slot : throw new ArgumentException($"Use a save slot from 0 to {SaveManager.ManualSlotCount}.");

        private static void RequirePlayerControl()
        {
            if (!Pal3Boot.HasPlayerControl()) throw new InvalidOperationException("PAL3 gameplay input is disabled.");
        }

        private static void RunSceneScript(string? argument)
        {
            var tokens = argument?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens is not { Length: 3 } || !int.TryParse(tokens[2], out int id) ||
                id < ScriptConstants.SystemScriptIdMax)
            {
                throw new ArgumentException("Use '<city> <scene> <scene-script-id>', e.g. 'q01 b 287'.");
            }

            var scene = ServiceLocator.Instance.Get<SceneManager>().GetCurrentScene();
            if (scene == null || !scene.GetSceneInfo().Is(tokens[0], tokens[1]))
            {
                throw new InvalidOperationException("The requested PAL3 scene is not loaded.");
            }

            Pal3Game.Instance.Execute(new Pal3.Core.Command.SceCommands.ScriptExecuteCommand(id));
        }

        public float GetCameraAxis(bool enabled)
        {
            if (!enabled) _cameraUntil = 0;
            return GameTimeProvider.Instance.TimeSinceStartup < _cameraUntil ? _cameraAxis : 0f;
        }

        private static (string Direction, float Seconds) ParseTimedDirection(string? argument)
        {
            var tokens = argument?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens is not { Length: 2 } ||
                !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ||
                !float.IsFinite(seconds) || seconds <= 0f || seconds > 5f)
            {
                throw new ArgumentException("Use a direction and a duration in seconds, e.g. 'right 1' (max 5 seconds).");
            }
            return (tokens[0].ToLowerInvariant(), seconds);
        }

        private static void ScheduleMovement(string? argument)
        {
            var (input, seconds) = ParseTimedDirection(argument);
            UnityEngine.Vector2 direction = input switch
            {
                "up" => new(0, 1),
                "down" => new(0, -1),
                "left" => new(-1, 0),
                "right" => new(1, 0),
                _ => throw new ArgumentException("Direction must be up, down, left or right.")
            };
            ServiceLocator.Instance.Get<PlayerGamePlayManager>().MovePlayerFor(direction, seconds);
        }

        private static void MoveToTile(string? argument)
        {
            var tokens = argument?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens is not { Length: 2 or 3 } ||
                !int.TryParse(tokens[0], out int x) || !int.TryParse(tokens[1], out int y) ||
                (tokens.Length == 3 && tokens[2] is not ("walk" or "run")))
            {
                throw new ArgumentException("Use '<tile-x> <tile-y> [walk|run]', e.g. '81 357 run'.");
            }

            var services = ServiceLocator.Instance;
            var scene = services.Get<SceneManager>().GetCurrentScene();
            int actorId = services.Get<PlayerActorManager>().GetPlayerActorId();
            var movement = scene?.GetActorGameEntity(actorId)?.GetComponent<ActorMovementController>();
            if (scene == null || movement == null ||
                !scene.GetTilemap().TryGetTile(new UnityEngine.Vector2Int(x, y), movement.GetCurrentLayerIndex(), out var tile) ||
                !tile.IsWalkable())
            {
                throw new InvalidOperationException("The destination is not walkable on the player's current layer.");
            }

            services.Get<PlayerGamePlayManager>().MovePlayerFor(default, 0f);
            var game = Pal3Game.Instance;
            game.Execute(new ActorStopActionAndStandCommand(actorId));
            game.Execute(new ActorPathToCommand(actorId, x, y, tokens.Length == 3 && tokens[2] == "run" ? 1 : 0));
        }

        public string ExecuteRemote(string command, string argument, Action afterTransition)
        {
            try
            {
                object result = Execute(new Request(command, argument, DateTimeOffset.MaxValue), afterTransition);
                return JsonSerializer.Serialize(new { ok = true, result }, JsonOptions);
            }
            catch (Exception e)
            {
                return JsonSerializer.Serialize(new { ok = false, error = e.GetBaseException().Message }, JsonOptions);
            }
        }

        private static string Select(string[] choices, string? argument)
        {
            if (int.TryParse(argument, out int number) && number >= 1 && number <= choices.Length)
            {
                return choices[number - 1];
            }
            return choices.FirstOrDefault(choice => choice == argument)
                ?? throw new ArgumentException("Unknown selection. Run list and use its 1-based index or exact name.");
        }

        public static void RunTransition(string target, Action action)
        {
            target = target.Replace('\n', ' ').Replace('\r', ' ');
            var timer = Stopwatch.StartNew();
            Debug.Log($"[PAL3 transition] begin {target}");
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PAL3 transition] failed {target} ({timer.ElapsedMilliseconds} ms): {e}");
                throw;
            }
            try
            {
                Debug.Log($"[PAL3 transition] dispatched {target} ({timer.ElapsedMilliseconds} ms): {JsonSerializer.Serialize(Snapshot(), JsonOptions)}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PAL3 transition] dispatched {target}; snapshot unavailable: {e.Message}");
            }
        }

        private static object Snapshot()
        {
            bool autoDialogue = Pal3Boot.AutoDialogueEnabled;
            if (!Pal3Boot.IsGameReady) return new
            {
                state = "Booting",
                autoDialogue,
                bootError = Pal3Boot.BootError,
                storagePermissionRequired = Pal3Boot.StoragePermissionRequired
            };

            var services = ServiceLocator.Instance;
            var sceneManager = services.Get<SceneManager>();
            var scene = sceneManager.GetCurrentScene();
            var info = scene?.GetSceneInfo();
            var player = services.Get<PlayerActorManager>();
            int actorId = player.GetPlayerActorId();
            var scripts = services.Get<ScriptManager>().GetScriptStatus();
            var saves = services.Get<SaveManager>();
            var caption = services.Get<CaptionRenderer>();
            bool playerHasControl = Pal3Boot.HasPlayerControl();
            var gameState = services.Get<GameStateManager>().GetCurrentState();
            return new
            {
                state = sceneManager.IsLoading ? "Loading" :
                    Pal3Boot.WaitingForMenuStart && gameState != GameState.VideoPlaying ? "Title" : gameState.ToString(),
                city = info?.CityName,
                scene = info?.SceneName,
                story = services.Get<IUserVariableStore<ushort, int>>().Get(ScriptConstants.MainStoryVariableId),
                player = DescribeActor(sceneManager.IsLoading ? null : scene, actorId),
                playerHasControl,
                cameraCanRotate = playerHasControl && services.Get<CameraManager>().CanRotateByUser,
                cameraYaw = Pal3Boot.CameraActor?.ActorRotation.Yaw,
                autoDialogue,
                dialogue = DialoguePresentation.Text,
                captionOpacity = caption.DisplayTexture != null ? caption.Opacity : (float?)null,
                hasLocalSceneLight = UnityEngine.SceneLighting.LocalLightPosition.HasValue,
                choices = DialoguePresentation.Choices,
                choicePrompt = DialoguePresentation.ChoicePrompt,
                diagnosticsVisible = Engine.Diagnostics.DebugOverlay.IsOverlayVisible,
                team = services.Get<TeamManager>().GetActorsInTeam().Select(actor => actor.ToString()).Order().ToArray(),
                scripts,
                loadingScene = sceneManager.IsLoading,
                loadingPhase = sceneManager.LoadingPhase,
                loadingProgress = sceneManager.LoadingProgress,
                sceneLoadError = sceneManager.LastLoadError,
                loadingSave = saves.IsRestoring,
                saveLoadError = saves.LastLoadError,
                simulationSeconds = Engine.Services.GameTimeProvider.Instance.TimeSinceStartup
            };
        }

        private static object DescribeActor(GameScene? scene, int actorId)
        {
            try
            {
                var entity = scene?.GetActorGameEntity(actorId);
                var movement = entity?.GetComponent<ActorMovementController>();
                var tile = movement?.GetTilePosition();
                var position = entity?.Transform.Position;
                return new
                {
                    id = actorId, tileX = tile?.x, tileY = tile?.y,
                    x = position?.x, y = position?.y, z = position?.z,
                    layer = movement?.GetCurrentLayerIndex(),
                    action = entity?.GetComponent<ActorActionController>()?.GetCurrentAction()
                };
            }
            catch (Exception e)
            {
                return new { id = actorId, unavailable = e.GetBaseException().Message };
            }
        }

        private static object SampleActorPose(string? argument)
        {
            if (!int.TryParse(argument, out int actorId) || actorId < 0)
            {
                throw new ArgumentException("Use a non-negative PAL3 actor ID.");
            }

            var scene = Pal3Boot.IsGameReady ? ServiceLocator.Instance.Get<SceneManager>().GetCurrentScene() : null;
            var entity = scene?.GetActorGameEntity(actorId);
            var action = entity?.GetComponent<ActorActionController>();
            var position = entity?.Transform.Position;
            var forward = entity?.Transform.Forward;
            string[] fingerprints = action is VertexAnimationActorActionController vertexAction
                ? vertexAction.GetMeshPoseFingerprints() : Array.Empty<string>();
            return new
            {
                actorId,
                action = action?.GetCurrentAction(),
                x = position?.x,
                y = position?.y,
                z = position?.z,
                forward = forward is { } direction ? new { direction.x, direction.y, direction.z } : null,
                fingerprints,
                simulationSeconds = GameTimeProvider.Instance.TimeSinceStartup
            };
        }

        private static void WriteJson(string path, object value)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
    }
}
