using System.Windows.Threading;

namespace BrainPlatform.Desktop.Shared.Notifications;

public enum OperationNotificationKind
{
    Success,
    Information,
    Warning,
    Error,
}

/// <summary>
/// One UI-facing source for completed user operations. Page status text remains
/// contextual; this object answers the separate question: what just happened?
/// </summary>
public sealed class OperationNotificationCenter : ObservableObject
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(2);
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer autoDismissTimer;
    private string message = string.Empty;
    private OperationNotificationKind kind = OperationNotificationKind.Success;
    private bool isVisible;
    private long revision;
    private long autoDismissRevision;

    public OperationNotificationCenter()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        autoDismissTimer = new DispatcherTimer { Interval = DisplayDuration };
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
        OperationNotificationKind.Information => "提示",
        OperationNotificationKind.Warning => "请注意",
        OperationNotificationKind.Error => "操作失败",
        _ => "提示",
    };

    public void PublishSuccess(string message) => Publish(OperationNotificationKind.Success, message);

    public void PublishInformation(string message) => Publish(OperationNotificationKind.Information, message);

    public void PublishWarning(string message) => Publish(OperationNotificationKind.Warning, message);

    public void PublishError(string message) => Publish(OperationNotificationKind.Error, message);

    public void Dismiss()
    {
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(Dismiss);
            return;
        }

        autoDismissTimer.Stop();
        IsVisible = false;
    }

    private void Publish(OperationNotificationKind notificationKind, string value)
    {
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => Publish(notificationKind, value));
            return;
        }

        var nextRevision = Interlocked.Increment(ref revision);
        autoDismissTimer.Stop();
        Message = value.Trim();
        Kind = notificationKind;
        IsVisible = !string.IsNullOrWhiteSpace(Message);
        if (IsVisible)
        {
            autoDismissRevision = nextRevision;
            autoDismissTimer.Interval = DisplayDuration;
            autoDismissTimer.Start();
        }
    }

    private void OnAutoDismissTimerTick(object? sender, EventArgs eventArgs)
    {
        autoDismissTimer.Stop();
        if (autoDismissRevision == Volatile.Read(ref revision))
        {
            IsVisible = false;
        }
    }
}
