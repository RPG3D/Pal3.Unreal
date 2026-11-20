using UnrealSharp.Core;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;

namespace ManagedSharpDemo;

internal static class Pal3UiText
{
    public static FText Get(string key, string nativeText) =>
        URuntimeJsonWidgetLibrary.GetLocalizedText(key, nativeText);

    public static FText Format(string key, string nativeText, params FFormatArgumentData[] arguments)
    {
        using var pattern = Get(key, nativeText);
        return TextLibrary.Format(pattern, arguments);
    }

    public static FFormatArgumentData Text(string name, FText value) => new()
    {
        ArgumentName = name,
        ArgumentValueType = EFormatArgumentType.Text,
        ArgumentValue = value
    };

    public static FFormatArgumentData Number(string name, int value) => new()
    {
        ArgumentName = name,
        ArgumentValueType = EFormatArgumentType.Int,
        ArgumentValue = FText.None,
        ArgumentValueInt = value
    };

    public static void Set(URuntimeJsonWidget widget, string id, string key, string nativeText,
        params FFormatArgumentData[] arguments)
    {
        using var value = arguments.Length == 0 ? Get(key, nativeText) : Format(key, nativeText, arguments);
        widget.SetElementTextValue(id, value);
    }
}
