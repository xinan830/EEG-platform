using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

public partial class AnalysisExecutionCard : UserControl
{
    public AnalysisExecutionCard()
    {
        InitializeComponent();
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSliderDragStarted), true);
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSliderDragCompleted), true);
    }

    private void OnSliderDragStarted(object sender, DragStartedEventArgs args)
    {
        if (DataContext is AnalysisContextViewModel context)
            context.BeginDynamicPreviewSeek();
    }

    private void OnSliderDragCompleted(object sender, DragCompletedEventArgs args)
    {
        if (DataContext is AnalysisContextViewModel context)
            context.CommitDynamicPreviewSeek();
    }
}
