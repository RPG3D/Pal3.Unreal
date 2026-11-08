using System.Text.Json;
using Engine.Services;
using Pal3.Game.Dev;
using Pal3.Game.GameSystems.Dialogue;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.UMG;

namespace ManagedSharpDemo
{
    internal sealed class Pal3DialogueChoices : IDisposable
    {
        private const int PageSize = 4;
        private const int PanelWidth = 800;
        private const int Inset = 24;
        private const int RowHeight = 80;
        private const int RowGap = 16;
        private IReadOnlyList<string>? _choices;
        private URuntimeJsonWidget? _widget;
        private int _page;

        public void Tick(APlayerController pc)
        {
            var choices = DialoguePresentation.Choices;
            if (!ReferenceEquals(choices, _choices))
            {
                _choices = choices;
                _page = 0;
                if (choices.Count == 0)
                {
                    if (_widget == null) return;
                    Dispose();
                    pc.ShowMouseCursor = false;
                    WidgetLibrary.SetInputMode_GameOnly(pc, flushInput: true);
                    return;
                }
                Show(choices, 0);
            }

            if (_widget == null) return;
            string id = Pal3UiLayout.ConsumeClick(_widget);
            if (id == "previous" && _page > 0)
            {
                Show(choices, _page - 1);
            }
            else if (id == "next" && (_page + 1) * PageSize < choices.Count)
            {
                Show(choices, _page + 1);
            }
            else if (int.TryParse(id, out int index))
            {
                ServiceLocator.Instance.Get<DialogueManager>().TryChoose(index);
            }
        }

        private void Show(IReadOnlyList<string> choices, int page)
        {
            int first = page * PageSize;
            int count = Math.Min(PageSize, choices.Count - first);
            var rows = new List<object>();
            string prompt = DialoguePresentation.ChoicePrompt;
            int firstRow = prompt.Length == 0 ? 68 : 124;
            rows.Add(new
            {
                type = "text", text = prompt.Length == 0 ? "请选择" : prompt, fontSize = 26, wrap = true,
                position = new[] { Inset, 14 }, size = new[] { PanelWidth - Inset * 2, firstRow - 24 }
            });
            for (int i = 0; i < count; i++)
            {
                int index = first + i;
                rows.Add(Button(index.ToString(), $"{index + 1}. {choices[index]}",
                    Inset, firstRow + i * (RowHeight + RowGap), PanelWidth - Inset * 2));
            }

            int height = firstRow + count * (RowHeight + RowGap);
            if (choices.Count > PageSize)
            {
                const int pageButtonWidth = 164;
                const int pageButtonHeight = 64;
                if (page > 0) rows.Add(Button("previous", "上一页", Inset, height, pageButtonWidth, pageButtonHeight));
                if (first + count < choices.Count)
                    rows.Add(Button("next", "下一页", PanelWidth - Inset - pageButtonWidth, height,
                        pageButtonWidth, pageButtonHeight));
                height += pageButtonHeight + RowGap;
            }

            string layout = JsonSerializer.Serialize(new
            {
                root = new
                {
                    type = "canvas",
                    children = new[]
                    {
                        new
                        {
                            type = "border", anchor = "center", size = new[] { PanelWidth, height },
                            color = new[] { 0.035, 0.055, 0.075, 0.98 },
                            child = new { type = "canvas", children = rows }
                        }
                    }
                }
            });

            var widget = URuntimeJsonWidgetLibrary.CreateJsonWidget(layout, 110);
            if (widget == null)
            {
                UnrealSharp.Log.UnrealLogger.LogError("Pal3", "Unable to display the PAL3 dialogue choices widget.");
                return;
            }
            Dispose();
            _widget = widget;
            _page = page;
        }

        private static object Button(string id, string text, int x, int y, int width, int height = RowHeight) => new
        {
            type = "button", id, text, fontSize = 24, wrap = true,
            position = new[] { x, y }, size = new[] { width, height },
            normalColor = new[] { 0.11, 0.22, 0.29, 1.0 },
            hoverColor = new[] { 0.18, 0.36, 0.43, 1.0 }
        };

        public void Dispose()
        {
            if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
            _widget = null;
        }
    }
}
