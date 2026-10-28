namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

using System.Text.Json;
using System.IO;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.Montages;
using BrainPlatform.Desktop.Modules.Review.Playback;

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
        runCoordinator.SupersedeForeground(configurationRevision);
        if (LastRun is { Status: "queued" or "running" } obsoleteRun)
            _ = CancelObsoleteRunAsync(obsoleteRun);
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

        var profiles = await montageStore.LoadAsync(CancellationToken.None);
        var manifestPath = Path.Combine(SourceDirectory.Trim(), "manifest.json");
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<LocalRawRecordingManifest>(
            manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, CancellationToken.None)
            ?? throw new InvalidOperationException("记录清单为空，无法验证兼容导联。");
        var montageCatalog = RecordingMontageCatalog.Build(manifest, profiles.Where(profile => !profile.IsArchived));
        var acquisitionProfile = montageCatalog.AcquisitionMontage;
        var compatibleProfiles = montageCatalog.CompatibleViewingMontages.Select(item => item.Profile)
            .Where(profile => acquisitionProfile is null ||
                !string.Equals(profile.Id, acquisitionProfile.Id, StringComparison.Ordinal) ||
                !string.Equals(profile.Fingerprint, acquisitionProfile.Fingerprint, StringComparison.Ordinal))
            .ToArray();
        RegisteredRecording = await runCoordinator.RegisterRecordingAsync(
            SourceDirectory.Trim(), CancellationToken.None, compatibleProfiles);
        if (RegisteredRecording.Channels.Count == 0 || RegisteredRecording.DurationSeconds is not > 0)
            throw new InvalidOperationException("注册记录没有有效的通道或时长。");

        RegisteredChannels.Clear();
        MontageProfiles.Clear();
        foreach (var profile in HistoricalMontageCatalogue.Deduplicate(
                     RegisteredRecording.MontageCatalogue ?? []))
            MontageProfiles.Add(profile);
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
        SelectedMontageProfile = MontageProfiles.FirstOrDefault(profile => profile.IsAcquisitionDefault && profile.Available)
            ?? MontageProfiles.FirstOrDefault(profile => profile.Available);
        if (SelectedMontageProfile is null)
            SelectedChannel = RegisteredChannels[0];
        SelectedF4Channel = RegisteredChannels.Count > 1 ? RegisteredChannels[1] : string.Empty;
        ApplyAlgorithmChannelDefaults();
        RefreshTnkRoleChannels();
        EndSecondsText = RegisteredRecording.DurationSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        StatusText = $"已注册记录：{RegisteredRecording.OriginalName}，{RegisteredRecording.DurationSeconds.Value:0.###} 秒。请选择分析导联和范围。";
    }

    private void ApplyAlgorithmChannelDefaults()
    {
        if (RegisteredChannels.Count == 0)
            return;

        if (SelectedAlgorithm?.Id.StartsWith("tnk_", StringComparison.OrdinalIgnoreCase) == true)
        {
            var primaryRole = SelectedAlgorithm.Parameters.FirstOrDefault(parameter => parameter.Key == "channel")?.LabelZh
                .Replace(" 分析通道", string.Empty, StringComparison.Ordinal).Trim();
            var tnkPrimary = FindRegisteredChannel(primaryRole);
            if (tnkPrimary is not null)
                SelectedChannel = tnkPrimary;
        }

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

    private void RefreshTnkRoleChannels()
    {
        TnkRoleChannels.Clear();
        if (!string.Equals(SelectedAlgorithm?.Id, "tnk_biofeedback", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var role in new[] { "Fz", "Pz", "Oz", "F3", "F4" })
        {
            var preferredChannel = SelectedMontageProfile?.Outputs.FirstOrDefault(output => output.Available &&
                string.Equals(output.Role, role, StringComparison.OrdinalIgnoreCase))?.Id;
            preferredChannel ??= RegisteredChannels.FirstOrDefault(channel =>
                string.Equals(channel, role, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, role + "-REF", StringComparison.OrdinalIgnoreCase));
            var selection = new TnkRoleChannelSelection(role, $"{role} 来源通道", preferredChannel ?? string.Empty);
            selection.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TnkRoleChannelSelection.SelectedChannel))
                    ScheduleAutomaticRun();
            };
            TnkRoleChannels.Add(selection);
        }
    }
}
