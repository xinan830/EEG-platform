using System.Collections.ObjectModel;

namespace BrainPlatform.Desktop.Modules.Projects.ViewModels;

public sealed record RecordingHistoryRow(
    string StartedAt,
    string DeviceName,
    string SamplingRate,
    int ChannelCount,
    string Status,
    string RecordingDirectory);

public sealed class RecordingHistoryViewModel : ObservableObject
{
    private readonly LocalRecordingCatalog catalog;
    private string statusText = "尚未加载本地记录。";

    public RecordingHistoryViewModel(LocalRecordingCatalog? catalog = null)
    {
        this.catalog = catalog ?? new LocalRecordingCatalog();
    }

    public ObservableCollection<RecordingHistoryRow> Records { get; } = [];

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public void Refresh(string recordingRoot)
    {
        var records = catalog.Read(recordingRoot);
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(new RecordingHistoryRow(
                record.RecordingStartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                record.DeviceName,
                $"{record.SamplingRateHz} Hz",
                record.SignalChannelCount,
                record.Status,
                record.RecordingDirectory));
        }

        StatusText = records.Count == 0 ? "当前目录没有可读取的记录。" : $"共 {records.Count} 条本地记录。";
    }
}
