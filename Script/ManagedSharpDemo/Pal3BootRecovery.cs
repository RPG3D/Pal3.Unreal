using Pal3.Core.Contract.Constants;
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
            _widget.SetElementVisible("boot_permission", Pal3Boot.StoragePermissionRequired || Pal3Boot.CanSelectPal3Folder);
            if (Pal3Boot.CanSelectPal3Folder) _widget.SetElementText("boot_permission", "选择 PAL3 文件夹");
        }

        if (Pal3Boot.CanSelectPal3Folder)
        {
            switch (UProjectStorageLibrary.ConsumePal3FolderSelectionResult())
            {
                case EPal3FolderSelectionResult.Selected:
                    _shownError = null;
                    Pal3Boot.RunFullBoot();
                    return true;
                case EPal3FolderSelectionResult.MissingData:
                    _widget.SetElementText("boot_error", "该文件夹中没有可读取的 basedata/basedata.cpk。请确认原版数据已下载到本机，再选择 PAL3 文件夹。");
                    break;
                case EPal3FolderSelectionResult.Unavailable:
                    _widget.SetElementText("boot_error", "无法访问所选文件夹。请重新选择 PAL3 文件夹。");
                    break;
            }
        }

        switch (Pal3UiLayout.ConsumeClick(_widget))
        {
            case "boot_permission" when Pal3Boot.CanSelectPal3Folder:
                UProjectStorageLibrary.RequestPal3FolderSelection(FileConstants.BaseDataCpkFileRelativePath);
                break;
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
