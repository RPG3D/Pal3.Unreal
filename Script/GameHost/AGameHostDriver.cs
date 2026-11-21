using System.Text.Json;
using GameHost.Contracts;
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;
using UnrealSharp.SharpDemo;

namespace GameHost;

[UClass]
public partial class AGameHostDriver : AActor
{
    private GameCatalog? _catalog;
    private IGameSession? _session;
    private string? _runningGame;
    private URuntimeJsonWidget? _launcher;
    private string? _error;

    public override void BeginPlay()
    {
        base.BeginPlay();
        try
        {
            _catalog = new GameCatalog();
            GameHostServices.Catalog = _catalog;
            if (!UnrealSharp.Engine.SystemLibrary.ParseParam(UnrealSharp.Engine.SystemLibrary.CommandLine, "GameHostAutostart"))
            {
                ShowLauncher();
                return;
            }
            // Selection starts a game session; ordinary UnrealSharp startup has
            // already loaded both code variants without opening either data set.
            StartSelected();
        }
        catch (Exception error)
        {
            _error = error.Message;
            UnrealSharp.Log.UnrealLogger.LogError("GameHost", error.ToString());
            ShowLauncher();
        }
    }

    private void StartSelected()
    {
        if (_session != null) throw new InvalidOperationException("A game session is already running.");
        if (_catalog == null) throw new InvalidOperationException("Game catalog is unavailable.");
        GameDescriptor game = _catalog.Games.Single(game => game.Id == _catalog.SelectedGame);
        IGameModule module = GameModuleLoader.Load(game);
        IGameSession? session = null;
        try { session = module.CreateSession(); session.Start(); }
        catch (Exception error)
        {
            try { session?.Dispose(); }
            catch (Exception cleanup) { UnrealSharp.Log.UnrealLogger.LogError("GameHost", cleanup.ToString()); }
            throw new InvalidOperationException("Game session initialization failed; restart the application before retrying. " + error.Message, error);
        }
        _session = session;
        _runningGame = module.GameId;
        _error = null;
    }

    private void ShowLauncher()
    {
        if (_launcher == null)
        {
            const string path = "Script/GameHost/Resources/GameLauncher.json";
            if (!UProjectFileLibrary.ReadText(Path.Combine(UProjectFileLibrary.ProjectDirectory, path), out string json))
            {
                UnrealSharp.Log.UnrealLogger.LogError("GameHost", "Cannot read game-list layout: " + path);
                return;
            }
            _launcher = URuntimeJsonWidgetLibrary.CreateJsonWidget(
                GameLauncherLayout.BindGames(json, _catalog?.Games ?? Array.Empty<GameDescriptor>()), 150);
        }
        if (_launcher == null) return;
        if (_error != null) _launcher.SetElementText("host_error", _error);
        else
        {
            using var hint = LocalizedValue("host.hint", "点选上方游戏，再点击开始");
            _launcher.SetElementTextValue("host_error", hint);
        }
        using var currentTitle = _catalog is null ? LocalizedValue("host.none", "无")
            : GameTitleValue(_catalog.Games.First(game => game.Id == _catalog.SelectedGame));
        using var current = FormatGame("host.current", "当前选择：{Game}", currentTitle);
        _launcher.SetElementTextValue("host_selected", current);
        if (_catalog != null)
            foreach (GameDescriptor game in _catalog.Games)
            {
                // Keep the FText history alive in UMG. Editor preview culture
                // changes can refresh display strings after this frame; taking
                // ToString() here would freeze the previous language in a row.
                using var title = GameTitleValue(game);
                if (game.Id == _catalog.SelectedGame)
                {
                    using var label = SelectedGameTitle(title);
                    _launcher.SetElementTextValue(GameLauncherLayout.GameButtonPrefix + game.Id, label);
                }
                else _launcher.SetElementTextValue(GameLauncherLayout.GameButtonPrefix + game.Id, title);
            }
    }

    private static FText LocalizedValue(string key, string nativeText) =>
        URuntimeJsonWidgetLibrary.GetLocalizedText(key, nativeText);

    private static FText GameTitleValue(GameDescriptor game) => LocalizedValue("host.game." + game.Id, game.Title);

    private static FText FormatGame(string key, string nativeText, FText title)
    {
        using var pattern = LocalizedValue(key, nativeText);
        return TextLibrary.Format(pattern, [new FFormatArgumentData {
            ArgumentName = "Game", ArgumentValueType = EFormatArgumentType.Text, ArgumentValue = title
        }]);
    }

    private static FText SelectedGameTitle(FText title)
    {
        using var prefix = LocalizedValue("host.selected", "已选 · ");
        using var pattern = new FText("{Selection}{Game}");
        return TextLibrary.Format(pattern, [
            new FFormatArgumentData { ArgumentName = "Selection", ArgumentValueType = EFormatArgumentType.Text, ArgumentValue = prefix },
            new FFormatArgumentData { ArgumentName = "Game", ArgumentValueType = EFormatArgumentType.Text, ArgumentValue = title }
        ]);
    }

    private static string Localized(string key, string nativeText)
    {
        using var text = URuntimeJsonWidgetLibrary.GetLocalizedText(key, nativeText);
        return text.ToString();
    }

    private static string GameTitle(GameDescriptor game) => Localized("host.game." + game.Id, game.Title);

    private string[] DisplayedTexts()
    {
        var texts = new List<string>();
        void Visit(UWidget? widget)
        {
            if (widget is UTextBlock label)
            {
                using var text = label.Text;
                texts.Add(text.ToString());
            }
            if (widget is UPanelWidget panel)
                for (int index = 0; index < panel.ChildrenCount; index++) Visit(panel.GetChildAt(index));
        }
        Visit(_launcher?.WidgetTree.RootWidget);
        return texts.ToArray();
    }

    private void SelectGame(string id)
    {
        if (_catalog == null) throw new InvalidOperationException("Game catalog is unavailable.");
        _catalog.Select(id);
        _error = null;
        if (_launcher != null) ShowLauncher();
    }

    public override void Tick(float deltaTime)
    {
        base.Tick(deltaTime);
        if (_launcher == null || _catalog == null) return;
        if (!_launcher.ConsumeClickedButton(out var id)) return;
        try
        {
            HandleLauncherAction(id.ToString());
        }
        catch (Exception error) { _error = error.Message; ShowLauncher(); }
    }

    private void HandleLauncherAction(string action)
    {
        if (_launcher == null) throw new InvalidOperationException("Game list is not visible.");
        if (action.StartsWith(GameLauncherLayout.GameButtonPrefix, StringComparison.Ordinal))
            SelectGame(action[GameLauncherLayout.GameButtonPrefix.Length..]);
        else if (action == "host_language")
        {
            string language = URuntimeJsonWidgetLibrary.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
            if (!URuntimeJsonWidgetLibrary.SetLanguage(language)) throw new InvalidOperationException("Language is unavailable: " + language);
            ShowLauncher();
        }
        else if (action == "host_start")
        {
            StartSelected();
            CloseLauncher();
        }
        else throw new ArgumentException("Unknown game-list action: " + action);
    }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public string ExecuteGameCommand(string command, string argument)
    {
        if (command == "host-status") return JsonSerializer.Serialize(new { selectedGame = _catalog?.SelectedGame, runningGame = _runningGame, running = _session != null, error = _error });
        if (command.StartsWith("host-", StringComparison.Ordinal))
        {
            try
            {
                if (_catalog == null) throw new InvalidOperationException("Game catalog is unavailable.");
                switch (command)
                {
                    case "host-games":
                        return JsonSerializer.Serialize(new { ok = true, result = _catalog.Games.Select(game => new { id = game.Id, title = game.Title, available = _catalog.IsAvailable(game.Id) }) });
                    case "host-select":
                        SelectGame(argument);
                        break;
                    case "host-click":
                        HandleLauncherAction(argument);
                        break;
                    case "host-ui":
                        object Bounds(string id)
                        {
                            bool visible = _launcher != null && _launcher.TryGetElementViewportRect(id, out _, out _);
                            if (!visible) return new { visible = false };
                            _launcher!.TryGetElementViewportRect(id, out var origin, out var size);
                            return new { visible, x = origin.X, y = origin.Y, width = size.X, height = size.Y };
                        }
                        return JsonSerializer.Serialize(new { ok = true, result = new {
                            visible = _launcher != null, viewport = Bounds("host_game_viewport"), language = URuntimeJsonWidgetLibrary.Language,
                            displayedTexts = DisplayedTexts(),
                            languageButton = Bounds("host_language"),
                            screen = new { width = UWidgetLayoutLibrary.ViewportSize.X, height = UWidgetLayoutLibrary.ViewportSize.Y },
                            games = _catalog.Games.Select(game => new { id = game.Id, title = GameTitle(game),
                                selected = game.Id == _catalog.SelectedGame, bounds = Bounds(GameLauncherLayout.GameButtonPrefix + game.Id) }),
                            nextButton = Bounds("host_next") } });
                    case "host-start":
                        StartSelected();
                        CloseLauncher();
                        break;
                    default: throw new ArgumentException("Unknown host command: " + command);
                }
                return JsonSerializer.Serialize(new { ok = true });
            }
            catch (Exception error)
            {
                _error = error.Message;
                if (_session == null) ShowLauncher();
                return JsonSerializer.Serialize(new { ok = false, error = _error });
            }
        }
        return _session?.ExecuteCommand(command, argument)
            ?? JsonSerializer.Serialize(new { ok = false, error = _error ?? "No active game session." });
    }

    private void CloseLauncher()
    {
        if (_launcher != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_launcher);
        _launcher = null;
        var pc = UGameplayStatics.GetPlayerController(0);
        if (pc != null) WidgetLibrary.SetInputMode_GameOnly(pc, flushInput: true);
    }

    public override void EndPlay(EEndPlayReason endPlayReason)
    {
        _session?.Dispose();
        _session = null;
        _runningGame = null;
        if (_launcher != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_launcher);
        _launcher = null;
        base.EndPlay(endPlayReason);
    }
}
