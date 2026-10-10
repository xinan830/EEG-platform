using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>Moves output rows without changing physical inputs or reference rules.</summary>
public sealed class MontageOutputOrder
{
    private readonly ObservableCollection<MontageDraftRow> rows;
    private readonly Func<bool> canEdit;
    private readonly MoveCommand up;
    private readonly MoveCommand down;

    public MontageOutputOrder(ObservableCollection<MontageDraftRow> rows, Func<bool> canEdit)
    {
        this.rows = rows;
        this.canEdit = canEdit;
        up = new MoveCommand(this, -1);
        down = new MoveCommand(this, 1);
        rows.CollectionChanged += (_, _) => Refresh();
    }

    public ICommand MoveUpCommand => up;
    public ICommand MoveDownCommand => down;

    public void Refresh()
    {
        up.RaiseCanExecuteChanged();
        down.RaiseCanExecuteChanged();
    }

    private bool CanMove(MontageDraftRow? row, int offset)
    {
        var index = row is null ? -1 : rows.IndexOf(row);
        return canEdit() && index >= 0 && index + offset >= 0 && index + offset < rows.Count;
    }

    private void Move(MontageDraftRow? row, int offset)
    {
        if (!CanMove(row, offset)) return;
        var index = rows.IndexOf(row!);
        rows.Move(index, index + offset);
        for (var position = 0; position < rows.Count; position++) rows[position].DisplayOrder = position;
        Refresh();
    }

    private sealed class MoveCommand(MontageOutputOrder owner, int offset) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => owner.CanMove(parameter as MontageDraftRow, offset);
        public void Execute(object? parameter) => owner.Move(parameter as MontageDraftRow, offset);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
