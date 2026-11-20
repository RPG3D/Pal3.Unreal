using Engine.Diagnostics;
using Engine.Services;
using Pal3.Core.Contract.Constants;
using Pal3.Game;
using Pal3.Game.Data;
using Pal3.Game.Dev;
using Pal3.Game.Scene;
using Pal3.Game.State;
using UnrealSharp.Engine;
using UnrealSharp.CoreUObject;
using UnrealSharp.InputCore;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;
using UnrealSharp.Core;

namespace ManagedSharpDemo
{
    internal sealed class Pal3StartMenu(Pal3TouchStick touchStick)
    {
        private enum Page { Home, Scenes, Story, Saves }

        private const int StoriesPerPage = 8;
        private static readonly Dictionary<string, string> SceneAreaNames = SceneConstants.SceneCpkNameInfos
            .Where(info => !string.IsNullOrWhiteSpace(info.sceneName))
            .ToDictionary(info => info.cpkName[..^4], info => info.sceneName, StringComparer.OrdinalIgnoreCase);
        internal static readonly string[] Stories = DevCommands.StoryJumpPoints.Keys.ToArray();
        // Spawn tiles from the upstream presets for these PAL3 scenes.
        internal static readonly (string Label, string City, string Scene, int X, int Y)[] Scenes =
        [
            ("渝州 · 唐家堡方向", "q01", "b", 195, 212),
            ("渝州西南 · 客栈", "q01", "xa", 215, 47),
            ("宾化", "q02", "q02", 70, 209),
            ("镇江", "q03", "q03", 325, 165),
        ];

        private URuntimeJsonWidget? _widget;
        private Page _page;
        private int _storyPage;
        private (string City, string Scene)[]? _sceneEntries;
        private Task<(string City, string Scene)[]>? _sceneQuery;
        private bool _openScenesWhenReady;
        private string? _sceneArea;
        private int _selectedSaveSlot;
        private bool _canTransition;
        private FText? _status;
        private int? _overwriteSlot;

        // Returns true when gameplay keyboard input should be suppressed this frame.
        public bool Tick(APlayerController pc, bool toggleRequested = false)
        {
            if (Pal3Boot.IsGameReady && ServiceLocator.Instance.Get<SceneManager>().IsLoading)
            {
                Close(pc);
                return true;
            }
            bool atTitle = Pal3Boot.WaitingForMenuStart;
            bool tab = toggleRequested || pc.WasInputKeyJustPressed(Keys.Tab);
            GameState? state = Pal3Boot.IsGameReady
                ? ServiceLocator.Instance.Get<GameStateManager>().GetCurrentState() : null;
            if (state == GameState.VideoPlaying)
            {
                Close(pc);
                return false;
            }
            bool canTransition = atTitle || state == GameState.Gameplay;
            bool canOpen = canTransition || state is GameState.Cutscene or GameState.Combat;
            if (!canOpen)
            {
                bool wasOpen = _widget != null;
                Close(pc);
                return wasOpen;
            }

            if (_widget == null)
            {
                _canTransition = canTransition;
                if (atTitle && ServiceLocator.Instance.Get<SaveManager>().LastLoadError is { } error)
                {
                    _status = FormatLoadError(error);
                    Show(pc, Page.Saves);
                }
                else if (atTitle || tab)
                {
                    Show(pc, Page.Home);
                }
                return atTitle || _widget != null;
            }

            string id = Pal3UiLayout.ConsumeClick(_widget);
            if (tab || id == "menu_tab")
            {
                if (!atTitle) Close(pc);
                else if (_page != Page.Home) Navigate(pc, Page.Home);
                return true;
            }

            if (_canTransition != canTransition)
            {
                _canTransition = canTransition;
                Navigate(pc, Page.Home);
                return true;
            }

            bool enter = atTitle && _page == Page.Home && pc.WasInputKeyJustPressed(Keys.Enter);
            if (enter)
            {
                Launch(pc, "new game", Pal3Boot.StartFromMenu);
                return true;
            }

            if (pc.WasInputKeyJustPressed(Keys.Escape) || id == "menu_escape")
            {
                if (_page == Page.Scenes) BackFromScenes(pc);
                else if (_page != Page.Home) Navigate(pc, Page.Home);
                else if (!atTitle) Close(pc);
                return true;
            }

            if (!string.IsNullOrEmpty(id))
            {
                HandleClick(pc, id);
            }

            CompleteSceneQuery(pc);
            return true;
        }

        private void HandleClick(APlayerController pc, string id)
        {
            switch (id)
            {
                case "diagnostics":
                    DebugOverlay.IsOverlayVisible = !DebugOverlay.IsOverlayVisible;
                    Show(pc, _page);
                    return;
                case "auto":
                    Pal3Boot.AutoDialogueEnabled = !Pal3Boot.AutoDialogueEnabled;
                    Show(pc, _page);
                    return;
                case "transparent_sticks" when Pal3TouchStick.IsSupported:
                    touchStick.FullyTransparent = !touchStick.FullyTransparent;
                    Show(pc, _page);
                    return;
                case "language":
                    string language = URuntimeJsonWidgetLibrary.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                        ? "zh-CN" : "en";
                    URuntimeJsonWidgetLibrary.SetLanguage(language);
                    return;
                case "resume": Close(pc); return;
            }
            if (!_canTransition) return;

            switch (id)
            {
                case "new": Launch(pc, "new game", Pal3Boot.StartFromMenu); return;
                case "scenes": OpenScenes(pc); return;
                case "story": _storyPage = 0; Navigate(pc, Page.Story); return;
                case "saves": Navigate(pc, Page.Saves); return;
                case "load_selected" when _page == Page.Saves:
                    int loadSlot = _selectedSaveSlot;
                    if (ServiceLocator.Instance.Get<SaveManager>().SaveSlotExists(loadSlot))
                        Launch(pc, $"load slot {loadSlot}", () => Pal3Boot.LoadFromSlot(loadSlot));
                    return;
                case "save_selected" when _page == Page.Saves && CanWriteSelectedSlot:
                    SaveSlot(pc, _selectedSaveSlot);
                    return;
                case "maze_entrance" or "maze_exit":
                    if (GetAvailableMazeSkipper() is { } skipper)
                    {
                        bool entrance = id == "maze_entrance";
                        Launch(pc, entrance ? "maze entrance" : "maze exit or story point",
                            entrance ? skipper.PortalToEntrance : skipper.PortalToExitOrNextStoryPoint);
                    }
                    return;
                case "prev": ChangeStoryPage(pc, -1); return;
                case "next": ChangeStoryPage(pc, 1); return;
                case "scene_back": BackFromScenes(pc); return;
                case "story_back" or "saves_back": Navigate(pc, Page.Home); return;
            }

            if (_page == Page.Scenes && _sceneEntries is { } entries)
            {
                if (_sceneArea == null && id.StartsWith("area_", StringComparison.Ordinal))
                {
                    string area = id[5..];
                    if (entries.Any(entry => entry.City == area))
                    {
                        _sceneArea = area;
                        _status = null;
                        Show(pc, Page.Scenes, rebuild: true);
                    }
                }
                else if (_sceneArea is { } city && id.StartsWith("scene_", StringComparison.Ordinal))
                {
                    string scene = id[6..];
                    if (entries.Contains((city, scene)))
                    {
                        var preset = Scenes.FirstOrDefault(entry => entry.City == city && entry.Scene == scene);
                        Launch(pc, $"{city}/{scene}", () =>
                        {
                            if (preset.City != null) Pal3Boot.SwitchScene(city, scene, preset.X, preset.Y);
                            else Pal3Boot.SwitchScene(city, scene);
                        });
                    }
                }
            }
            else if (id.StartsWith("story_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(6), out int storySlot) && (uint)storySlot < StoriesPerPage &&
                _storyPage * StoriesPerPage + storySlot < Stories.Length)
            {
                string checkpoint = Stories[_storyPage * StoriesPerPage + storySlot];
                Launch(pc, checkpoint, () => Pal3Boot.StartFromCheckpoint(checkpoint));
            }
            else if (_page == Page.Saves && id.StartsWith("slot_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(5), out int slot) && slot is >= 0 and <= SaveManager.ManualSlotCount)
            {
                SelectSaveSlot(slot);
            }
        }

        private bool CanWriteSelectedSlot => _selectedSaveSlot > SaveManager.AutoSaveSlotIndex && Pal3Boot.HasPlayerControl();

        private void SelectSaveSlot(int slot)
        {
            if (_widget is not { } widget || slot == _selectedSaveSlot) return;
            int previousSlot = _selectedSaveSlot;
            _selectedSaveSlot = slot;
            _overwriteSlot = null;
            _status = null;
            var saves = ServiceLocator.Instance.Get<SaveManager>();
            RefreshSaveSlotRow(widget, saves, previousSlot);
            RefreshSaveSlotRow(widget, saves, slot);
            RefreshSaveSelection(widget, saves);
            RefreshStatus(widget);
        }

        private void SaveSlot(APlayerController pc, int slot)
        {
            try
            {
                if (_overwriteSlot != slot && ServiceLocator.Instance.Get<SaveManager>().SaveSlotExists(slot))
                {
                    _overwriteSlot = slot;
                    _status = Pal3UiText.Format("save.confirm_status", "再次点击存档 {Slot} 确认覆盖。",
                        Pal3UiText.Number("Slot", slot));
                }
                else
                {
                    _overwriteSlot = null;
                    Pal3Boot.SaveToSlot(slot);
                    _status = Pal3UiText.Format("save.written", "已写入存档 {Slot}。",
                        Pal3UiText.Number("Slot", slot));
                }
            }
            catch (Exception e)
            {
                _status = new FText(e.GetBaseException().Message);
                DebugOverlay.Error("[PAL3 save] " + e);
            }
            Show(pc, Page.Saves);
        }

        private void ChangeStoryPage(APlayerController pc, int offset)
        {
            int page = _storyPage + offset;
            if (page < 0 || page * StoriesPerPage >= Stories.Length) return;
            _storyPage = page;
            Navigate(pc, Page.Story);
        }

        private void Navigate(APlayerController pc, Page page)
        {
            _openScenesWhenReady = false;
            _status = null;
            _overwriteSlot = null;
            Show(pc, page);
        }

        private void BackFromScenes(APlayerController pc)
        {
            if (_sceneArea == null)
            {
                Navigate(pc, Page.Home);
                return;
            }

            _sceneArea = null;
            _status = null;
            Show(pc, Page.Scenes, rebuild: true);
        }

        private string BuildSceneLayout()
        {
            var entries = _sceneEntries ?? throw new InvalidOperationException("场景目录尚未加载。");
            var destinations = _sceneArea is { } area
                ? entries.Where(entry => entry.City == area)
                    .Select(entry => ($"scene_{entry.Scene}", entry.Scene.ToUpperInvariant())).ToArray()
                : entries.Select(entry => entry.City).Distinct()
                    .OrderBy(city => city[0] == 'q' ? 0 : 1).ThenBy(city => city, StringComparer.Ordinal)
                    .Select(city => ($"area_{city}", SceneAreaNames.GetValueOrDefault(city) ?? city.ToUpperInvariant())).ToArray();
            string title = _sceneArea is { } city ? $"切换场景 · {SceneAreaLabel(city)}" : "切换 PAL3 场景";
            return Pal3TravelMenuLayout.Build(title, destinations, "scene_back", "返回", "没有可切换的场景",
                new { Tab = "menu_tab", Escape = "menu_escape" });
        }

        private static string SceneAreaLabel(string city) => SceneAreaNames.TryGetValue(city, out string? name)
            ? $"{name} {city.ToUpperInvariant()}" : city.ToUpperInvariant();

        private void OpenScenes(APlayerController pc)
        {
            _sceneArea = null;
            if (_sceneEntries != null)
            {
                Navigate(pc, Page.Scenes);
                return;
            }

            if (_sceneQuery == null)
            {
                // Resolve services on the game thread. The provider caches the background query for this session.
                var resources = ServiceLocator.Instance.Get<GameResourceProvider>();
                _sceneQuery = resources.ListSceneEntriesAsync();
            }
            _openScenesWhenReady = true;
            _status = null;
            if (_widget is { } widget)
            {
                RefreshHome(widget);
                RefreshStatus(widget);
            }
        }

        private void CompleteSceneQuery(APlayerController pc)
        {
            if (_sceneQuery is not { IsCompleted: true } query) return;
            _sceneQuery = null;
            bool openScenes = _openScenesWhenReady && _widget != null && _page == Page.Home;
            _openScenesWhenReady = false;
            try
            {
                _sceneEntries = query.GetAwaiter().GetResult();
                if (openScenes) Navigate(pc, Page.Scenes);
            }
            catch (Exception error)
            {
                DebugOverlay.Error("[PAL3 scene catalog] " + error);
                if (openScenes)
                    _status = Pal3UiText.Format("menu.query_failed", "地图查询失败 · {Reason}",
                        Pal3UiText.Text("Reason", new FText(error.GetBaseException().Message)));
            }
            if (_widget is { } widget && _page == Page.Home)
            {
                RefreshHome(widget);
                RefreshStatus(widget);
            }
        }

        private static void AddSceneQuerySpinner(URuntimeJsonWidget widget)
        {
            if (widget.WidgetTree.RootWidget is not UCanvasPanel root ||
                root.GetChildAt(0) is not UBorder { Content: UCanvasPanel panel } ||
                panel.GetChildAt(0) is not UWidgetSwitcher pages ||
                pages.GetWidgetAtIndex(0) is not UCanvasPanel home ||
                home.GetChildAt(home.ChildrenCount - 1) is not UCanvasPanel holder ||
                holder.Slot is not UCanvasPanelSlot holderSlot)
                return;

            var spinner = UObject.NewObject<UCircularThrobber>(widget.WidgetTree);
            spinner.Radius = 12f;
            spinner.Period = 0.8f;
            spinner.Visibility = ESlateVisibility.HitTestInvisible;
            holder.AddChildToCanvas(spinner).Size = holderSlot.Size;
        }

        private void Launch(APlayerController pc, string target, Action action)
        {
            try
            {
                Pal3Automation.RunTransition(target, action);
            }
            catch (Exception e)
            {
                _status = FormatLoadError(e.GetBaseException().Message);
                Show(pc, _page);
                return;
            }
            Close(pc);
        }

        private static FText FormatLoadError(string reason)
        {
            reason = reason.Replace('\n', ' ').Replace('\r', ' ');
            return Pal3UiText.Format("menu.load_failed", "加载失败 · {Reason}",
                Pal3UiText.Text("Reason", new FText(reason.Length > 46 ? reason[..46] + "…" : reason)));
        }

        private void Show(APlayerController pc, Page page, bool rebuild = false)
        {
            bool standalone = page is Page.Saves or Page.Scenes;
            bool opening = rebuild || _widget == null ||
                (page != _page && (standalone || _page is Page.Saves or Page.Scenes));
            try
            {
                if (opening)
                {
                    string? layoutPath = Environment.GetEnvironmentVariable("PAL3_MENU_LAYOUT");
                    var nextWidget = page switch
                    {
                        Page.Saves => URuntimeJsonWidgetLibrary.CreateJsonWidget(Pal3SaveMenuLayout.Json, 100),
                        Page.Scenes => URuntimeJsonWidgetLibrary.CreateJsonWidget(BuildSceneLayout(), 100),
                        _ => Pal3UiLayout.Create("Pal3StartMenu", 100, interactive: true, overridePath: layoutPath)
                    };
                    if (nextWidget == null) throw new InvalidOperationException("无法显示 PAL3 菜单。");
                    RemoveWidget();
                    _widget = nextWidget;
                    if (!standalone) AddSceneQuerySpinner(nextWidget);
                    if (standalone) Pal3UiLayout.ConfigureMenuScroll(nextWidget);
                    if (page == Page.Saves) _selectedSaveSlot = SaveManager.AutoSaveSlotIndex;
                }

                var widget = _widget;
                if (widget == null) return;

                switch (page)
                {
                    case Page.Home:
                        RefreshHome(widget);
                        break;
                    case Page.Story:
                        RefreshStory(widget);
                        break;
                    case Page.Saves:
                        RefreshSaves(widget);
                        break;
                }

                if (!standalone)
                {
                    string pageId = page switch
                    {
                        Page.Home => "home_page",
                        Page.Story => "story_page",
                        _ => throw new ArgumentOutOfRangeException(nameof(page))
                    };
                    if (!widget.SetSwitcherPage("menu_pages", pageId))
                        throw new InvalidOperationException($"Missing PAL3 menu page '{pageId}' in menu_pages.");
                }

                _page = page;
                RefreshStatus(widget);
            }
            catch (Exception e)
            {
                if (opening) Close(pc);
                DebugOverlay.Error("[PAL3 menu] " + e);
            }
        }

        private void RefreshStatus(URuntimeJsonWidget widget)
        {
            widget.SetElementTextValue("menu_status", _status ?? FText.None);
            widget.SetElementVisible("menu_status", _status != null);
        }

        public void RemoveWidget()
        {
            _openScenesWhenReady = false;
            if (_widget == null) return;
            URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
            _widget = null;
        }

        public void Close(APlayerController pc)
        {
            if (_widget == null) return;
            RemoveWidget();
            _status = null;
            _overwriteSlot = null;
            _page = Page.Home;
            _sceneArea = null;
            pc.ShowMouseCursor = false;
            WidgetLibrary.SetInputMode_GameOnly(pc, flushInput: true);
        }

        private void RefreshHome(URuntimeJsonWidget widget)
        {
            if (_openScenesWhenReady) Pal3UiText.Set(widget, "scenes", "menu.querying", "正在查询地图…");
            else Pal3UiText.Set(widget, "scenes", "menu.scenes", "切换 PAL3 场景");
            widget.SetElementVisible("scene_scan_spinner", _openScenesWhenReady);
            bool atTitle = Pal3Boot.WaitingForMenuStart;
            if (atTitle) Pal3UiText.Set(widget, "home_title", "game.title", "仙剑奇侠传三");
            else Pal3UiText.Set(widget, "home_title", "menu.title", "PAL3 主菜单");
            string hintKey = Pal3TouchStick.IsSupported
                ? (atTitle ? "menu.hint_touch_title" : "menu.hint_touch_game")
                : (atTitle ? "menu.hint_keyboard_title" : "menu.hint_keyboard_game");
            string hint = Pal3TouchStick.IsSupported
                ? (atTitle ? "开始游戏后点 Menu 打开菜单" : "点返回游戏关闭菜单")
                : (atTitle ? "开始游戏后按 Tab 打开菜单" : "Tab 返回游戏");
            Pal3UiText.Set(widget, "home_hint", hintKey, hint);
            Pal3UiText.Set(widget, "diagnostics", "menu.diagnostics_state", "诊断面板 · {State}",
                Pal3UiText.Text("State", OnOff(DebugOverlay.IsOverlayVisible)));
            Pal3UiText.Set(widget, "auto", "menu.auto_state", "自动推进对白 · {State}",
                Pal3UiText.Text("State", OnOff(Pal3Boot.AutoDialogueEnabled)));
            widget.SetElementVisible("transparent_sticks", Pal3TouchStick.IsSupported);
            if (Pal3TouchStick.IsSupported)
                Pal3UiText.Set(widget, "transparent_sticks", "menu.sticks_state", "摇杆全透明 · {State}",
                    Pal3UiText.Text("State", OnOff(touchStick.FullyTransparent)));
            widget.SetElementVisible("resume", !atTitle);
            widget.SetElementVisible("new", _canTransition);
            widget.SetElementVisible("saves", _canTransition);
            widget.SetElementVisible("scenes", _canTransition);
            widget.SetElementVisible("story", _canTransition);
            bool canSkipMaze = GetAvailableMazeSkipper() != null;
            widget.SetElementVisible("maze_entrance", canSkipMaze);
            widget.SetElementVisible("maze_exit", canSkipMaze);
        }

        private static MazeSkipper? GetAvailableMazeSkipper()
        {
            if (!Pal3Boot.HasPlayerControl()) return null;
            var services = ServiceLocator.Instance;
            if (services.Get<SceneManager>().GetCurrentScene() is not { } scene) return null;
            var skipper = services.Get<MazeSkipper>();
            return skipper.IsMazeSceneAndHasSkipperCommands(scene.GetSceneInfo()) ? skipper : null;
        }

        private void RefreshStory(URuntimeJsonWidget widget)
        {
            int first = _storyPage * StoriesPerPage;
            for (int i = 0; i < StoriesPerPage; i++)
            {
                string id = $"story_{i}";
                bool hasStory = first + i < Stories.Length;
                widget.SetElementVisible(id, hasStory);
                if (hasStory)
                {
                    string label = Stories[first + i].Replace("\n\n", " · ").Replace('\n', ' ');
                    widget.SetElementText(id, label);
                }
            }

            int pages = Math.Max(1, (Stories.Length + StoriesPerPage - 1) / StoriesPerPage);
            widget.SetElementVisible("prev", _storyPage > 0);
            widget.SetElementVisible("next", _storyPage + 1 < pages);
            widget.SetElementText("story_page_number", $"{_storyPage + 1} / {pages}");
        }

        private void RefreshSaves(URuntimeJsonWidget widget)
        {
            var saves = ServiceLocator.Instance.Get<SaveManager>();
            for (int slot = SaveManager.AutoSaveSlotIndex; slot <= SaveManager.ManualSlotCount; slot++)
                RefreshSaveSlotRow(widget, saves, slot);
            RefreshSaveSelection(widget, saves);
        }

        private void RefreshSaveSlotRow(URuntimeJsonWidget widget, SaveManager saves, int slot)
        {
            string selection = slot == _selectedSaveSlot ? "> " : "";
            using var summary = saves.SaveSlotExists(slot)
                ? new FText($"{saves.GetSaveSlotLastWriteTime(slot):MM-dd HH:mm}")
                : Pal3UiText.Get("save.empty", "空");
            using var name = GetSaveSlotName(slot);
            Pal3UiText.Set(widget, $"slot_{slot}", "save.row", "{Selection}{SlotName}  ·  {Summary}",
                Pal3UiText.Text("Selection", new FText(selection)),
                Pal3UiText.Text("SlotName", name), Pal3UiText.Text("Summary", summary));
        }

        private void RefreshSaveSelection(URuntimeJsonWidget widget, SaveManager saves)
        {
            int slot = _selectedSaveSlot;
            bool exists = saves.SaveSlotExists(slot);
            bool canSave = CanWriteSelectedSlot;
            using var name = GetSaveSlotName(slot);
            widget.SetElementTextValue("selected_slot_title", name);
            if (exists)
                Pal3UiText.Set(widget, "selected_slot_status", "save.saved_at", "保存于 {Time}",
                    Pal3UiText.Text("Time", new FText($"{saves.GetSaveSlotLastWriteTime(slot):MM-dd HH:mm}")));
            else Pal3UiText.Set(widget, "selected_slot_status", "save.empty_slot", "空存档");
            widget.SetElementVisible("load_selected", exists);
            widget.SetElementVisible("save_selected", canSave);
            if (_overwriteSlot == slot) Pal3UiText.Set(widget, "save_selected", "save.confirm", "确认覆盖存档");
            else Pal3UiText.Set(widget, "save_selected", "save.write", "写入存档");
            widget.SetElementVisible("save_hint", !canSave);
            if (slot == SaveManager.AutoSaveSlotIndex)
                Pal3UiText.Set(widget, "save_hint", "save.auto_hint", "由游戏自动写入");
            else Pal3UiText.Set(widget, "save_hint", "save.control_hint", "角色可操作时可存档");
        }

        private static FText GetSaveSlotName(int slot) => slot == SaveManager.AutoSaveSlotIndex
            ? Pal3UiText.Get("save.auto", "自动存档")
            : Pal3UiText.Format("save.slot", "存档 {Slot}", Pal3UiText.Number("Slot", slot));

        private static FText OnOff(bool on) => on
            ? Pal3UiText.Get("common.on", "开启") : Pal3UiText.Get("common.off", "关闭");
    }
}
