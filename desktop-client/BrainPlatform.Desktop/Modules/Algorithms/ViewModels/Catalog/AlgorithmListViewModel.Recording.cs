namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    private async Task AutoRegisterSelectedRecordingAsync()
    {
        try
        {
            await RegisterAsync();
            ScheduleAutomaticRun();
        }
        catch (Exception exception)
        {
            ReportRunError(exception);
        }
    }

    private void ScheduleAutomaticRun()
    {
        configurationRevision++;
        InvalidateBackgroundFullRunDisplay();
        if (IsOnDemandAnalysis) ClearWindowAnalysis();
        if (disposed || detailSuspended) return;
        if (SelectedProjectRecording is not { Status: "已完成" } ||
            RegisteredRecording is null ||
            SelectedAlgorithm is null ||
            string.IsNullOrWhiteSpace(SelectedChannel))
            return;

        automaticRunPending = true;
        automaticRunDebounceTimer.Stop();
        automaticRunDebounceTimer.Start();
    }

    private async Task RegisterAsync()
    {
        ResultSummaryText = "暂无结果摘要。";
        ProvenanceText = "暂无溯源信息。";
        StructuredPreviewText = "暂无结构化预览。";
        DynamicSeriesText = "暂无动态结果。";
        ClearPreviews();
        if (SelectedProjectRecording is not { Status: "已完成" } || string.IsNullOrWhiteSpace(SourceDirectory))
            throw new InvalidOperationException("请先从项目中选择一条已完成的记录。");

        RegisteredRecording = await runCoordinator.RegisterRecordingAsync(SourceDirectory.Trim(), CancellationToken.None);
        if (RegisteredRecording.Channels.Count == 0 || RegisteredRecording.DurationSeconds is not > 0)
            throw new InvalidOperationException("注册记录没有有效的通道或时长。");

        RegisteredChannels.Clear();
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        foreach (var channel in RegisteredRecording.Channels
                     .Where(channel => !string.IsNullOrWhiteSpace(channel))
                     .Select(channel => channel.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RegisteredChannels.Add(channel);
        }

        if (RegisteredChannels.Count == 0)
            throw new InvalidOperationException("注册记录没有可分析的 EEG 通道。");
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        SelectedChannel = RegisteredChannels[0];
        SelectedF4Channel = RegisteredChannels.Count > 1 ? RegisteredChannels[1] : string.Empty;
        ApplyAlgorithmChannelDefaults();
        EndSecondsText = RegisteredRecording.DurationSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        StatusText = $"已注册记录：{RegisteredRecording.OriginalName}，{RegisteredRecording.DurationSeconds.Value:0.###} 秒。请选择分析导联和范围。";
    }

    private void ApplyAlgorithmChannelDefaults()
    {
        if (RegisteredChannels.Count == 0)
            return;

        var descriptor = AlgorithmUiRegistry.For(SelectedAlgorithm?.Id ?? string.Empty);
        var primary = FindRegisteredChannel(descriptor.PreferredPrimaryChannel);
        var secondary = FindRegisteredChannel(descriptor.PreferredSecondaryChannel);
        if (primary is not null)
            SelectedChannel = primary;
        if (secondary is not null)
            SelectedF4Channel = secondary;
    }

    private void ApplyAlgorithmParameterDefaults()
    {
        var presetName = AlgorithmUiRegistry.For(SelectedAlgorithm?.Id ?? string.Empty).DefaultPeakBandPreset;
        if (presetName is null)
            return;

        applyingBandPreset = true;
        try
        {
            SelectedPeakBandPreset = presetName;
        }
        finally
        {
            applyingBandPreset = false;
        }
    }

    private string? FindRegisteredChannel(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
            return null;
        return RegisteredChannels.FirstOrDefault(channel =>
            string.Equals(channel, preferred, StringComparison.OrdinalIgnoreCase))
            ?? RegisteredChannels.FirstOrDefault(channel =>
                string.Equals(channel, preferred + "-REF", StringComparison.OrdinalIgnoreCase));
    }
}
