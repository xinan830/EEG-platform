using System.Windows.Input;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Settings.Services;

namespace BrainPlatform.Desktop.Modules.Settings.ViewModels;

public sealed class CacheMaintenanceViewModel : ObservableObject
{
    private readonly Func<Task<CacheClearResult>> clear;
    private bool isClearing;
    private string statusText = "清理后再次打开数据时，缓存会自动生成。";

    public CacheMaintenanceViewModel(Func<Task<CacheClearResult>> clear)
    {
        this.clear = clear;
        ClearCommand = new AsyncRelayCommand(ClearAsync, ReportError);
    }

    public ICommand ClearCommand { get; }
    public bool IsClearing
    {
        get => isClearing;
        private set
        {
            if (SetProperty(ref isClearing, value)) RaisePropertyChanged(nameof(ButtonText));
        }
    }
    public string ButtonText => IsClearing ? "正在清理…" : "清除缓存";
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

    public async Task ClearAsync()
    {
        if (IsClearing) return;
        IsClearing = true;
        StatusText = "正在清理回放缓存和动态算法结果…";
        try
        {
            var result = await clear();
            StatusText = $"已清除 {result.DeletedWindows} 个动态窗口、{result.DeletedFiles} 个缓存文件，释放 {result.FreedBytes / (1024d * 1024):F2} MB。";
        }
        catch (Exception exception) { ReportError(exception); }
        finally { IsClearing = false; }
    }

    private void ReportError(Exception exception)
    {
        DesktopLog.Error("cache_clear_failed", exception);
        StatusText = $"清理未完成：{exception.Message}";
    }
}
