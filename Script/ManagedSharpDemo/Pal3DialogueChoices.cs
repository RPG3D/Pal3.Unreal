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
        private const int PageSize = 6;
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
            int firstRow = prompt.Length == 0 ? 54 : 90;
            rows.Add(new
            {
                type = "text", text = prompt.Length == 0 ? "请选择" : prompt, fontSize = 21, wrap = true,
                position = new[] { 18, 12 }, size = new[] { 524, firstRow - 18 }
            });
            for (int i = 0; i < count; i++)
            {
                int index = first + i;
                rows.Add(Button(index.ToString(), $"{index + 1}. {choices[index]}", 18, firstRow + i * 51, 524));
            }

            int height = firstRow + 8 + count * 51;
            if (choices.Count > PageSize)
            {
                if (page > 0) rows.Add(Button("previous", "上一页", 18, height, 124));
                if (first + count < choices.Count) rows.Add(Button("next", "下一页", 418, height, 124));
                height += 52;
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
                            type = "border", anchor = "center", size = new[] { 560, height },
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

        private static object Button(string id, string text, int x, int y, int width) => new
        {
            type = "button", id, text, fontSize = 18, wrap = true,
            position = new[] { x, y }, size = new[] { width, 46 },
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
