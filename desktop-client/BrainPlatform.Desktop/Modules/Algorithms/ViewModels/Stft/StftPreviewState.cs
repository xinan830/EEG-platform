using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

/// <summary>STFT-only structured and parsed preview state.</summary>
public sealed class StftPreviewState : ObservableObject
{
    private StftPreview? result;
    private StructuredPreviewResponse? structuredPreview;

    public StftPreview? Result { get => result; set => SetProperty(ref result, value); }
    public StructuredPreviewResponse? StructuredPreview { get => structuredPreview; set => SetProperty(ref structuredPreview, value); }

    public void Clear()
    {
        Result = null;
        StructuredPreview = null;
    }
}
