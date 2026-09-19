using System.Windows.Threading;

namespace BrainPlatform.Desktop.ViewModels;

public enum OperationNotificationKind
{
    Success,
    Warning,
    Error,
}

/// <summary>
/// One UI-facing source for completed user operations. Page status text remains
/// contextual; this object answers the separate question: what just happened?
/// </summary>
public sealed class OperationNotificationCenter : ObservableObject
{
    private static readonly TimeSpan SuccessDisplayDuration = TimeSpan.FromSeconds(4);
    private readonly DispatcherTimer autoDismissTimer;
    private string message = string.Empty;
    private OperationNotificationKind kind = OperationNotificationKind.Success;
    private bool isVisible;
    private long revision;
    private long autoDismissRevision;

    public OperationNotificationCenter()
    {
        autoDismissTimer = new DispatcherTimer { Interval = SuccessDisplayDuration };
        autoDismissTimer.Tick += OnAutoDismissTimerTick;
    }

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    public OperationNotificationKind Kind
    {
        get => kind;
        private set
        {
            if (SetProperty(ref kind, value))
            {
                RaisePropertyChanged(nameof(KindLabel));
            }
        }
    }

    public bool IsVisible
    {
        get => isVisible;
        private set => SetProperty(ref isVisible, value);
    }

    public string KindLabel => Kind switch
    {
        OperationNotificationKind.Success => "已完成",
        OperationNotificationKind.Warning => "请注意",
        OperationNotificationKind.Error => "操作失败",
        _ => "提示",
    };

    public void PublishSuccess(string message) => Publish(OperationNotificationKind.Success, message);

    public void PublishWarning(string message) => Publish(OperationNotificationKind.Warning, message);

    public void PublishError(string message) => Publish(OperationNotificationKind.Error, message);

    public void Dismiss()
    {
        autoDismissTimer.Stop();
        IsVisible = false;
    }

    private void Publish(OperationNotificationKind notificationKind, string value)
    {
        var nextRevision = Interlocked.Increment(ref revision);
        autoDismissTimer.Stop();
        Message = value.Trim();
        Kind = notificationKind;
        IsVisible = !string.IsNullOrWhiteSpace(Message);
        if (IsVisible && notificationKind == OperationNotificationKind.Success)
        {
            autoDismissRevision = nextRevision;
            autoDismissTimer.Start();
        }
    }

    private void OnAutoDismissTimerTick(object? sender, EventArgs eventArgs)
    {
        autoDismissTimer.Stop();
        if (autoDismissRevision == Volatile.Read(ref revision) && Kind == OperationNotificationKind.Success)
        {
            IsVisible = false;
        }
    }
}
