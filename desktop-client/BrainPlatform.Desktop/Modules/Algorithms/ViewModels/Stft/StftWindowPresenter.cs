using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

internal static class StftWindowPresenter
{
    public static void Apply(StftPreviewState state, DynamicWindowResponse response)
    {
        state.Clear();
        var preview = response.State == "ready" ? StftPreview.FromWindow(response) : null;
        state.WindowPreview = response;
        state.Result = preview;
    }
}
