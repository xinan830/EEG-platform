using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis;

/// <summary>Owns non-modal floating windows spawned by <see cref="MultiAnalysisDockHost"/>.</summary>
internal sealed class MultiAnalysisFloatingWindowManager
{
    private readonly Func<Window?> ownerLookup;
    private readonly Func<DataTemplate?> templateLookup;
    private readonly Action<object> onUserClosed;
    private readonly Dictionary<object, Window> windows = [];
    private readonly HashSet<Window> closingProgrammatically = [];

    public MultiAnalysisFloatingWindowManager(
        Func<Window?> ownerLookup,
        Func<DataTemplate?> templateLookup,
        Action<object> onUserClosed)
    {
        this.ownerLookup = ownerLookup;
        this.templateLookup = templateLookup;
        this.onUserClosed = onUserClosed;
    }

    public void Sync(IEnumerable? floatingSlots)
    {
        var slots = MultiAnalysisDockHost.Materialize(floatingSlots);
        foreach (var stale in windows.Keys.Except(slots).ToArray())
            Close(stale);
        foreach (var slot in slots)
        {
            if (windows.ContainsKey(slot)) continue;
            var presenter = new ContentControl { Content = slot, ContentTemplate = templateLookup() };
            var title = MultiAnalysisDockHost.ReadStringProperty(slot, "DisplayName") ?? "算法分析";
            var window = new Window
            {
                Title = title,
                Width = 620,
                Height = 560,
                MinWidth = 440,
                MinHeight = 360,
                Owner = ownerLookup(),
                Content = presenter,
                DataContext = slot,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            window.Closed += (_, _) => OnWindowClosed(slot, window);
            windows[slot] = window;
            window.Show();
        }
    }

    public void UpdateTemplate(DataTemplate? template)
    {
        foreach (var window in windows.Values)
        {
            if (window.Content is ContentControl presenter) presenter.ContentTemplate = template;
        }
    }

    public void Activate(object slot)
    {
        if (!windows.TryGetValue(slot, out var window)) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void CloseAll()
    {
        foreach (var slot in windows.Keys.ToArray()) Close(slot);
    }

    private void Close(object slot)
    {
        if (!windows.Remove(slot, out var window)) return;
        closingProgrammatically.Add(window);
        window.Content = null;
        window.Close();
    }

    private void OnWindowClosed(object slot, Window window)
    {
        if (closingProgrammatically.Remove(window)) return;
        windows.Remove(slot);
        onUserClosed(slot);
    }
}
