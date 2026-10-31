using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

/// <summary>Coalesces TNK chart refresh callbacks on one WPF dispatcher turn.</summary>
internal sealed class TnkPlotRefreshScheduler
{
    private static readonly ConditionalWeakTable<Dispatcher, TnkPlotRefreshScheduler> Instances = new();
    private readonly Dispatcher dispatcher;
    private readonly object gate = new();
    private readonly HashSet<Action> pending = [];
    private bool scheduled;

    internal TnkPlotRefreshScheduler(Dispatcher dispatcher) => this.dispatcher = dispatcher;

    internal static TnkPlotRefreshScheduler For(Dispatcher dispatcher) =>
        Instances.GetValue(dispatcher, static key => new TnkPlotRefreshScheduler(key));

    internal void Request(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            pending.Add(callback);
            if (scheduled) return;
            scheduled = true;
        }

        try
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Drain));
        }
        catch (InvalidOperationException)
        {
            lock (gate)
            {
                scheduled = false;
                pending.Clear();
            }
        }
    }

    private void Drain()
    {
        Action[] callbacks;
        lock (gate)
        {
            callbacks = pending.ToArray();
            pending.Clear();
            scheduled = false;
        }

        foreach (var callback in callbacks)
            callback();
    }
}
