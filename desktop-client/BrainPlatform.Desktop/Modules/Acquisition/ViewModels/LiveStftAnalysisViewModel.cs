using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Live STFT heatmap presentation state matching Review STFT panel semantics.</summary>
public sealed class LiveStftAnalysisViewModel : ObservableObject
{
    private StftPreview? stft;
    private double? analysisStartSeconds;
    private string windowText = "";
    private string statusText = "等待可用的 10 秒窗口";

    public StftPreview? Stft
    {
        get => stft;
        private set
        {
            if (SetProperty(ref stft, value)) RaisePropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => Stft is not null;

    public double? AnalysisStartSeconds
    {
        get => analysisStartSeconds;
        private set => SetProperty(ref analysisStartSeconds, value);
    }

    public string WindowText
    {
        get => windowText;
        private set => SetProperty(ref windowText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public void Begin(double? startSeconds, string message = "等待可用的 10 秒窗口")
    {
        AnalysisStartSeconds = startSeconds;
        Stft = null;
        WindowText = "";
        StatusText = message;
    }

    public void Apply(LiveAnalysisUpdate update, string channel, string statusMessage)
    {
        StatusText = statusMessage;
        if (update.StartSeconds is not { } start || update.EndSeconds is not { } end)
        {
            WindowText = "";
            if (update.State is not ("ready" or "partial")) Stft = null;
            return;
        }
        WindowText = $"{start:0.###}–{end:0.###} s";
        if (update.State is not ("ready" or "partial"))
        {
            Stft = null;
            return;
        }
        Stft = LiveSpectralPresentation.ReadStft(update, channel, new TimeRange(start, end));
    }

    public void SetFault(string message)
    {
        Stft = null;
        WindowText = "";
        StatusText = message;
    }
}
