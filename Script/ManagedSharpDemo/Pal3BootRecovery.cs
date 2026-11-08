using Pal3.Game;
using UnrealSharp.Engine;
using UnrealSharp.RuntimeWidgetLayout;
using UnrealSharp.SharpDemo;
using UnrealSharp.UMG;

namespace ManagedSharpDemo;

internal sealed class Pal3BootRecovery : IDisposable
{
    private URuntimeJsonWidget? _widget;
    private string? _shownError;
    private float _untilPermissionCheck;

    public bool Tick(APlayerController player, float deltaTime)
    {
        if (Pal3Boot.StoragePermissionRequired)
        {
            _untilPermissionCheck -= deltaTime;
            if (_untilPermissionCheck <= 0f)
            {
                _untilPermissionCheck = .5f;
                if (UProjectStorageLibrary.HasSharedStorageAccess()) Pal3Boot.RunFullBoot();
            }
        }

        if (Pal3Boot.BootError is not { } error)
        {
            if (_widget != null)
            {
                Dispose();
                player.ShowMouseCursor = false;
                WidgetLibrary.SetInputMode_GameOnly(player, flushInput: true);
            }
            return false;
        }

        _widget ??= Pal3UiLayout.Create("Pal3BootRecovery", 130, interactive: true);
        if (_widget == null) return true;

        if (_shownError != error)
        {
            _shownError = error;
            _widget.SetElementText("boot_error", error);
            _widget.SetElementVisible("boot_permission", Pal3Boot.StoragePermissionRequired);
        }

        switch (Pal3UiLayout.ConsumeClick(_widget))
        {
            case "boot_permission":
                if (!UProjectStorageLibrary.RequestSharedStorageAccess())
                    _widget.SetElementText("boot_error", "无法打开授权设置。请在系统设置里授权后重试。");
                break;
            case "boot_retry":
                _shownError = null;
                Pal3Boot.RunFullBoot();
                break;
        }
        return true;
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
        _shownError = null;
    }
}
