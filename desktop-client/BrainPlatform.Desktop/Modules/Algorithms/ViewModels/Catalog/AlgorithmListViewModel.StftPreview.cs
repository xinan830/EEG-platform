using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private StftPreview? stftPreview;
    private StructuredPreviewResponse? stftStructuredPreview;

    public StftPreview? StftResult { get => stftPreview; private set => SetProperty(ref stftPreview, value); }
    public StructuredPreviewResponse? StftStructuredPreview { get => stftStructuredPreview; private set => SetProperty(ref stftStructuredPreview, value); }
    public bool HasStftPreview => StftResult is not null;
    public string StftAxisText => StftResult is null ? "时间 (s)" :
        $"请求范围 {StftResult.RequestedRange.StartSeconds:0.###} - {StftResult.RequestedRange.EndSeconds:0.###} s  ·  时频中心 {StftResult.TimesSeconds[0]:0.###} - {StftResult.TimesSeconds[^1]:0.###} s  ·  频率 {StftResult.FrequenciesHz[0]:0.###} - {StftResult.FrequenciesHz[^1]:0.###} Hz  ·  {StftResult.PowerUnit}";

    private async Task LoadStftStructuredPreviewAsync(string runId, bool dynamic)
    {
        try
        {
            var preview = await client.GetStructuredPreviewAsync(runId, 1_000_000, CancellationToken.None);
            StructuredPreviewText = AlgorithmResultFormatter.BuildStructuredPreview(preview);
            StftStructuredPreview = preview;
            if (dynamic)
                SetDynamicWindows(preview, "stft");
            else
                SetStftResult(StftPreview.Parse(preview));
        }
        catch (AlgorithmApiException exception) when (exception.Code == "REQUEST_INVALID")
        {
            StructuredPreviewText = "STFT 结果已保存，但预览超过 1,000,000 个数值单元或无法读取；请缩短分析范围后重新运行。";
            SetStftResult(null);
        }
        catch (AlgorithmApiException exception) when (exception.Code == "RESOURCE_NOT_FOUND")
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Code}：{exception.Message}";
            SetStftResult(null);
        }
        catch (Exception exception)
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Message}";
            SetStftResult(null);
        }
    }

    private void SetStftResult(StftPreview? preview)
    {
        StftResult = preview;
        RaisePropertyChanged(nameof(HasStftPreview));
        RaisePropertyChanged(nameof(StftAxisText));
    }

    private void ClearStftPreview()
    {
        SetStftResult(null);
        StftStructuredPreview = null;
    }

    private void UpdateDynamicStftPreview(int latestIndex, bool complete)
    {
        if (stftStructuredPreview is null) return;
        if (latestIndex >= 0 && complete)
        {
            try
            {
                SetStftResult(StftPreview.ParseDynamic(stftStructuredPreview, latestIndex));
                StructuredPreviewText = AlgorithmResultFormatter.BuildStructuredPreview(stftStructuredPreview);
                return;
            }
            catch (InvalidOperationException exception)
            {
                StructuredPreviewText = $"STFT 窗口不可显示：{exception.Message}";
            }
        }
        SetStftResult(null);
    }
}
