using System.Windows;
using BrainPlatform.Desktop.Modules.Acquisition.Views;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Projects.Api;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using RecordingReviewView = BrainPlatform.Desktop.Modules.Review.Views.RecordingReviewView;

namespace BrainPlatform.Desktop;

public partial class MainWindow
{
    private bool isOpeningReview;

    public async Task ShowRecordingReviewViewAsync(ProjectRecordingRow recording, IEnumerable<MontageProfile> profiles)
    {
        if (isOpeningReview)
        {
            reviewSessionWindow?.Activate();
            return;
        }
        isOpeningReview = true;
        try { await OpenRecordingReviewAsync(recording, profiles); }
        finally { isOpeningReview = false; }
    }

    private async Task OpenRecordingReviewAsync(
        ProjectRecordingRow recording,
        IEnumerable<MontageProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (reviewSessionWindow is { IsLoaded: true })
        {
            reviewSessionWindow.Activate();
            if (DataContext is DesktopWorkspaceViewModel activeWorkspace)
            {
                activeWorkspace.Notifications.PublishWarning("已有一个数据回溯窗口，请先关闭后再打开另一条记录。");
            }
            return;
        }

        var sourceId = recording.SourceKind == "backend" ? recording.RecordingId : recording.SessionId;
        if (!Guid.TryParseExact(sourceId, "N", out var expectedSessionId))
        {
            throw new RecordingReadException("RECORDING_IDENTITY_INVALID", "所选录音的会话标识无效。");
        }

        if (DataContext is not DesktopWorkspaceViewModel workspace)
            throw new InvalidOperationException("工作区尚未初始化，无法打开录音回溯。");

        IRecordingAccessSession loadedRecording;
        RegisteredRecording? registeredAnalysisRecording = null;
        if (recording.SourceKind == "backend")
        {
            var client = workspace.ResearchProjectClient;
            var project = workspace.Projects.SelectedProject ?? throw new InvalidOperationException("当前项目已失效。");
            var backendProject = await client.EnsureProjectAsync(project.Id, project.Name, project.Description, CancellationToken.None);
            var backendRecording = await client.GetRecordingAsync(recording.RecordingId!, CancellationToken.None);
            loadedRecording = new BackendRecordingAccessSession(client, backendRecording, backendProject);
            registeredAnalysisRecording = backendRecording.ToAnalysisDescriptor();
        }
        else
        {
            loadedRecording = await workspace.RecordingSessions.OpenLocalAsync(
                recording.RecordingDirectory, expectedSessionId, CancellationToken.None);
            try
            {
                await workspace.MontageConfigurations.RegisterRecordingReferenceAsync(
                    loadedRecording.Manifest.HardwareConfiguration, recording.RecordingDirectory);
            }
            catch (Exception exception)
            {
                workspace.Notifications.PublishWarning($"历史导联引用无法登记，记录仍可查看：{exception.Message}");
            }
        }
        var catalog = RecordingMontageCatalog.Build(
            loadedRecording.Manifest,
            recording.SourceKind == "backend" ? [] : profiles);
        var playbackCoordinator = new RecordingPlaybackTimeCoordinator(
            loadedRecording.Manifest.SessionId.ToString("N"),
            loadedRecording.Manifest.SamplingRateHz,
            checked(loadedRecording.LastSampleCounter - loadedRecording.FirstSampleCounter + 1L));
        RecordingReviewViewModel? viewModel = null;
        try
        {
            viewModel = new RecordingReviewViewModel(
                loadedRecording, catalog, recordingName: recording.Name, projectName: loadedRecording.Manifest.Project.Name,
                filter: recordingReviewFilter, recordingDirectory: recording.SourceKind == "backend" ? null : recording.RecordingDirectory,
                eventDefinitionService: workspace.Acquisition.EventDefinitionService, notifications: workspace.Notifications,
                algorithmClient: workspace.Algorithms, registeredAnalysisRecording: registeredAnalysisRecording,
                timeDisplaySettings: workspace.TimeDisplaySettings, playbackCoordinator: playbackCoordinator);
        }
        catch
        {
            if (viewModel is not null)
                await viewModel.DisposeAsync();
            playbackCoordinator.Dispose();
            await loadedRecording.DisposeAsync();
            throw;
        }

        recordingReviewRecording = loadedRecording;
        recordingReviewPlayback = playbackCoordinator;
        recordingReviewViewModel = viewModel;
        EegSessionWindow? openedWindow = null;
        try
        {
            var view = new RecordingReviewView { DataContext = viewModel };
            openedWindow = new EegSessionWindow(this, "数据回溯", view,
                SessionCloseRequest.Review, DisposeRecordingReviewAsync,
                () =>
                {
                    reviewSessionWindow = null;
                    ShowProjectDetailView();
                });
            reviewSessionWindow = openedWindow;
            openedWindow.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            if (ReferenceEquals(recordingReviewViewModel, viewModel)) await viewModel.InitializeAsync();
        }
        catch
        {
            if (ReferenceEquals(recordingReviewViewModel, viewModel))
            {
                await DisposeRecordingReviewAsync();
                openedWindow?.CloseCompletedSession();
            }
            throw;
        }
    }

    private async Task DisposeRecordingReviewAsync()
    {
        if (recordingReviewViewModel is { } viewModel)
        {
            recordingReviewViewModel = null;
            await viewModel.DisposeAsync();
        }

        if (recordingReviewRecording is { } loadedRecording)
        {
            recordingReviewRecording = null;
            await loadedRecording.DisposeAsync();
        }

        recordingReviewPlayback?.Dispose();
        recordingReviewPlayback = null;
    }
}
