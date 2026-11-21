using Pal3.Core.Contract.Constants;
using GameHost.Contracts;
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
            if (Pal3Boot.CanSelectPal3Folder) _widget.SetElementText("boot_permission", $"选择 {Pal3Boot.DataFolderDisplayName} 文件夹");
            UpdateVariantButton();
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
                    _widget.SetElementText("boot_error", $"该文件夹中没有可读取的 {Pal3Boot.RequiredDataFile}。请确认原版数据已下载到本机，再选择 {Pal3Boot.DataFolderDisplayName} 文件夹。");
                    break;
                case EPal3FolderSelectionResult.Unavailable:
                    _widget.SetElementText("boot_error", $"无法访问所选文件夹。请重新选择 {Pal3Boot.DataFolderDisplayName} 文件夹。");
                    break;
            }
        }

        switch (Pal3UiLayout.ConsumeClick(_widget))
        {
            case "boot_permission" when Pal3Boot.CanSelectPal3Folder:
                UProjectStorageLibrary.RequestFolderSelection(Pal3Boot.DataFolderStorageKey, Pal3Boot.RequiredDataFile);
                break;
            case "boot_permission":
                if (!UProjectStorageLibrary.RequestSharedStorageAccess())
                    _widget.SetElementText("boot_error", "无法打开授权设置。请在系统设置里授权后重试。");
                break;
            case "boot_retry":
                _shownError = null;
                Pal3Boot.RunFullBoot();
                break;
            case "boot_variant":
                SwitchVariant();
                break;
        }
        return true;
    }

    /// <summary>
    /// The boot error screen is the only UI a PAL3A-only installation can reach (the
    /// title menu needs a successful boot), so the variant switch has to live here too.
    /// Switching only rewrites the persisted choice; the next launch loads that set.
    /// </summary>
    private void SwitchVariant()
    {
        string current = GameHostServices.Catalog.SelectedGame.ToUpperInvariant();
        string next = current.Equals("PAL3A", StringComparison.OrdinalIgnoreCase) ? "PAL3" : "PAL3A";
        GameHostServices.Catalog.Select(next.ToLowerInvariant());
        UpdateVariantButton();

        bool available = GameHostServices.Catalog.IsAvailable(next);
        _widget?.SetElementText("boot_error", available
            ? $"已选择 {next}，重启应用后生效。"
            : $"已选择 {next}，但本安装包未包含该版本数据，重启后仍会回退。");
    }

    private void UpdateVariantButton()
    {
        if (_widget == null) return;
        string current = GameHostServices.Catalog.SelectedGame.ToUpperInvariant();
        bool available = GameHostServices.Catalog.IsAvailable(current);
        _widget.SetElementText("boot_variant", $"游戏版本：{current}{(available ? "" : " · 未安装")}");
    }

    public void Dispose()
    {
        if (_widget != null) URuntimeJsonWidgetLibrary.RemoveJsonWidget(_widget);
        _widget = null;
        _shownError = null;
    }
}
