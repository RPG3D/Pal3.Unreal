using Engine.Diagnostics;
using Engine.Services;
using Pal3.Game;
using Pal3.Game.Dev;
using Pal3.Game.Scene;
using Pal3.Game.State;
using UnrealSharp.Engine;
using UnrealSharp.InputCore;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo
{
    internal sealed class Pal3StartMenu(Pal3TouchStick touchStick)
    {
        private enum Page { Home, Scenes, Story, Saves }

        private const int StoriesPerPage = 8;
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
        private bool _canTransition;
        private string _status = "";
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
                if (atTitle || tab) Show(pc, Page.Home);
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
                if (_page != Page.Home) Navigate(pc, Page.Home);
                else if (!atTitle) Close(pc);
                return true;
            }

            if (!string.IsNullOrEmpty(id))
            {
                HandleClick(pc, id);
            }

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
                case "resume": Close(pc); return;
            }
            if (!_canTransition) return;

            switch (id)
            {
                case "new": Launch(pc, "new game", Pal3Boot.StartFromMenu); return;
                case "scenes": Navigate(pc, Page.Scenes); return;
                case "story": _storyPage = 0; Navigate(pc, Page.Story); return;
                case "saves": Navigate(pc, Page.Saves); return;
                case "prev": ChangeStoryPage(pc, -1); return;
                case "next": ChangeStoryPage(pc, 1); return;
                case "scene_back" or "story_back" or "saves_back": Navigate(pc, Page.Home); return;
            }

            if (id.StartsWith("scene_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(6), out int sceneIndex) && (uint)sceneIndex < Scenes.Length)
            {
                var scene = Scenes[sceneIndex];
                Launch(pc, $"{scene.City}/{scene.Scene}", () => Pal3Boot.SwitchScene(scene.City, scene.Scene, scene.X, scene.Y));
            }
            else if (id.StartsWith("story_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(6), out int storySlot) && (uint)storySlot < StoriesPerPage &&
                _storyPage * StoriesPerPage + storySlot < Stories.Length)
            {
                string checkpoint = Stories[_storyPage * StoriesPerPage + storySlot];
                Launch(pc, checkpoint, () => Pal3Boot.StartFromCheckpoint(checkpoint));
            }
            else if (id.StartsWith("load_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(5), out int loadSlot))
            {
                Launch(pc, $"load slot {loadSlot}", () => Pal3Boot.LoadFromSlot(loadSlot));
            }
            else if (id.StartsWith("save_", StringComparison.Ordinal) &&
                int.TryParse(id.AsSpan(5), out int saveSlot))
            {
                SaveSlot(pc, saveSlot);
            }
        }

        private void SaveSlot(APlayerController pc, int slot)
        {
            try
            {
                if (_overwriteSlot != slot && ServiceLocator.Instance.Get<SaveManager>().SaveSlotExists(slot))
                {
                    _overwriteSlot = slot;
                    _status = $"再次点击存档 {slot} 确认覆盖。";
                }
                else
                {
                    _overwriteSlot = null;
                    Pal3Boot.SaveToSlot(slot);
                    _status = $"已写入存档 {slot}。";
                }
            }
            catch (Exception e)
            {
                _status = e.GetBaseException().Message;
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
            _status = "";
            _overwriteSlot = null;
            Show(pc, page);
        }

        private void Launch(APlayerController pc, string target, Action action)
        {
            try
            {
                Pal3Automation.RunTransition(target, action);
            }
            catch (Exception e)
            {
                string reason = e.GetBaseException().Message.Replace('\n', ' ').Replace('\r', ' ');
                _status = "加载失败 · " + (reason.Length > 46 ? reason[..46] + "…" : reason);
                Show(pc, _page);
                return;
            }
            Close(pc);
        }

        private void Show(APlayerController pc, Page page)
        {
            bool opening = _widget == null;
            try
            {
                string pageId = page switch
                {
                    Page.Home => "home_page",
                    Page.Scenes => "scenes_page",
                    Page.Story => "story_page",
                    Page.Saves => "saves_page",
                    _ => throw new ArgumentOutOfRangeException(nameof(page))
                };

                if (opening)
                {
                    string? layoutPath = Environment.GetEnvironmentVariable("PAL3_MENU_LAYOUT");
                    _widget = Pal3UiLayout.Create("Pal3StartMenu", 100, interactive: true, overridePath: layoutPath);
                }

                var widget = _widget;
                if (widget == null) return;

                switch (page)
                {
                    case Page.Home:
                        RefreshHome(widget);
                        break;
                    case Page.Scenes:
                        for (int i = 0; i < Scenes.Length; i++)
                        {
                            widget.SetElementText($"scene_{i}", Scenes[i].Label);
                        }
                        break;
                    case Page.Story:
                        RefreshStory(widget);
                        break;
                    case Page.Saves:
                        RefreshSaves(widget);
                        break;
                }

                if (!widget.SetSwitcherPage("menu_pages", pageId))
                {
                    throw new InvalidOperationException($"Missing PAL3 menu page '{pageId}' in menu_pages.");
                }

                _page = page;
                widget.SetElementText("menu_status", _status);
                widget.SetElementVisible("menu_status", _status.Length > 0);
            }
            catch (Exception e)
            {
                if (opening) Close(pc);
                DebugOverlay.Error("[PAL3 menu] " + e);
            }
        }

        public void RemoveWidget()
        {
            if (_widget == null) return;
            URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
            _widget = null;
        }

        public void Close(APlayerController pc)
        {
            if (_widget == null) return;
            RemoveWidget();
            _status = "";
            _overwriteSlot = null;
            _page = Page.Home;
            pc.ShowMouseCursor = false;
            WidgetLibrary.SetInputMode_GameOnly(pc, flushInput: true);
        }

        private void RefreshHome(URuntimeJsonWidget widget)
        {
            bool atTitle = Pal3Boot.WaitingForMenuStart;
            widget.SetElementText("home_title", atTitle ? "仙剑奇侠传三" : "PAL3 主菜单");
            widget.SetElementText("home_hint", Pal3TouchStick.IsSupported
                ? (atTitle ? "开始游戏后点 Menu 打开菜单" : "点返回游戏关闭菜单")
                : (atTitle ? "开始游戏后按 Tab 打开菜单" : "Tab 返回游戏"));
            widget.SetElementText("diagnostics", DebugOverlay.IsOverlayVisible ? "诊断面板 · 开启" : "诊断面板 · 关闭");
            widget.SetElementText("auto", Pal3Boot.AutoDialogueEnabled ? "自动推进对白 · 开启" : "自动推进对白 · 关闭");
            widget.SetElementVisible("transparent_sticks", Pal3TouchStick.IsSupported);
            if (Pal3TouchStick.IsSupported)
                widget.SetElementText("transparent_sticks", touchStick.FullyTransparent ? "摇杆全透明 · 开启" : "摇杆全透明 · 关闭");
            widget.SetElementVisible("resume", !atTitle);
            widget.SetElementVisible("new", _canTransition);
            widget.SetElementVisible("saves", _canTransition);
            widget.SetElementVisible("scenes", _canTransition);
            widget.SetElementVisible("story", _canTransition);
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
            bool canSave = !Pal3Boot.WaitingForMenuStart && _canTransition;
            widget.SetElementText("save_heading", canSave ? "写入存档" : "进入游戏后可存档");
            for (int slot = 0; slot <= SaveManager.ManualSlotCount; slot++)
            {
                bool exists = saves.SaveSlotExists(slot);
                widget.SetElementVisible($"load_{slot}", exists);
                widget.SetElementVisible($"empty_{slot}", !exists);
                if (exists)
                {
                    string name = slot == 0 ? "自动存档" : $"存档 {slot}";
                    widget.SetElementText($"load_{slot}", $"{name}  {saves.GetSaveSlotLastWriteTime(slot):MM-dd HH:mm}");
                }
                if (slot > 0)
                {
                    widget.SetElementVisible($"save_{slot}", canSave);
                    widget.SetElementText($"save_{slot}", _overwriteSlot == slot ? $"确认覆盖 {slot}" : $"写入 {slot}");
                }
            }
        }
    }
}
