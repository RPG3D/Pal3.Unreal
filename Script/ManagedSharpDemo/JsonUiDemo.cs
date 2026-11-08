// JsonUiDemo.cs — demonstrates RuntimeWidgetLayout from C#:
//   JSON layout text  ->  URuntimeJsonWidget (C++ builds the UMG tree)
//                     ->  added to the viewport
// Button clicks are polled back (element id) instead of using managed delegates.
using UnrealSharp.RuntimeWidgetLayout;

namespace ManagedSharpDemo
{
    public static class JsonUiDemo
    {
        // Design-space coordinates (1:1 at 1080p). Canvas / border / text / button.
        private const string DemoJson = """
        {
          "root": {
            "type": "canvas",
            "children": [
              {
                "type": "border",
                "anchor": "top_left",
                "position": [40, 40],
                "size": [430, 160],
                "color": [0.04, 0.06, 0.09, 0.92],
                "padding": [12, 12, 12, 12],
                "child": {
                  "type": "canvas",
                  "children": [
                    {
                      "type": "text",
                      "text": "JSON Widget Demo",
                      "fontSize": 26,
                      "color": [1.0, 0.88, 0.55, 1.0],
                      "align": "left",
                      "position": [8, 6],
                      "size": [390, 34]
                    },
                    {
                      "type": "text",
                      "text": "RuntimeWidgetLayout module (JSON -> UMG)",
                      "fontSize": 16,
                      "color": [0.75, 0.80, 0.85, 1.0],
                      "align": "left",
                      "position": [8, 40],
                      "size": [390, 24]
                    },
                    {
                      "type": "button",
                      "id": "demo_ok",
                      "text": "点击测试按钮",
                      "fontSize": 20,
                      "position": [8, 74],
                      "size": [390, 52],
                      "normalColor": [0.12, 0.28, 0.52, 1.0],
                      "hoverColor": [0.22, 0.46, 0.82, 1.0],
                      "pressedColor": [0.08, 0.18, 0.36, 1.0]
                    }
                  ]
                }
              }
            ]
          }
        }
        """;

        private static URuntimeJsonWidget? _widget;

        public static bool IsVisible => _widget != null;

        public static void Show(int zOrder = 100)
        {
            Hide();
            try
            {
                _widget = URuntimeJsonWidgetLibrary.CreateJsonWidget(DemoJson, zOrder);
            }
            catch (System.Exception e)
            {
                Engine.Diagnostics.DebugOverlay.Error("[JsonUi] create failed: " + e.Message);
            }
        }

        public static void Hide()
        {
            if (_widget != null)
            {
                try { URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget); }
                catch (System.Exception) { }
                _widget = null;
            }
        }

        /// <summary>Polls the last clicked JSON element id (empty when nothing clicked).</summary>
        public static string PollClick()
        {
            if (_widget == null) return string.Empty;
            try
            {
                return _widget.ConsumeClickedButton(out string id) ? id : string.Empty;
            }
            catch (System.Exception)
            {
                _widget = null;
                return string.Empty;
            }
        }
    }
}
