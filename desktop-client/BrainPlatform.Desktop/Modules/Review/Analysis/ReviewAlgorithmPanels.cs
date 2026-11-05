using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Montages.Domain;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

internal static class ReviewAlgorithmPanels
{
    public static IReadOnlyDictionary<string, ReviewAlgorithmPanelRegistration> Create(
        RecordingReviewViewModel viewModel,
        WorkspaceMontageSelection<MontageProfile> montageSelection)
    {
        var registrations = new[]
        {
            new ReviewAlgorithmPanelRegistration(montageSelection, "psd", "功率谱密度", ReviewAnalysisPanelKind.Psd,
                viewModel.PsdParameters, viewModel.PsdAnalysis,
                (recording, algorithm, montage) =>
                {
                    viewModel.PsdParameters.Initialize(recording, algorithm, montage);
                    viewModel.PsdAnalysis.SetChannels(viewModel.PsdParameters.Channels);
                },
                (duration, rate, montage) => viewModel.PsdParameters.BuildConfig(duration, rate, montage),
                (recording, _, montage) => viewModel.PsdParameters.RefreshInputOptions(recording, montage),
                (response, _) => viewModel.PsdAnalysis.Apply(response),
                client => new PsdReviewAnalysisAdapter(client)),
            new ReviewAlgorithmPanelRegistration(montageSelection, "rbp", "RBP 相对频段功率", ReviewAnalysisPanelKind.Rbp,
                viewModel.RbpParameters, viewModel.RbpAnalysis,
                (recording, algorithm, montage) =>
                {
                    viewModel.RbpParameters.Initialize(recording, algorithm, montage);
                    viewModel.RbpAnalysis.InitializeChannels(viewModel.RbpParameters.ResolveAnalysisChannels(montage));
                },
                (duration, rate, montage) => viewModel.RbpParameters.BuildConfig(duration, rate, montage),
                (recording, previous, montage) => viewModel.RbpParameters.RefreshInputOptions(recording, previous, montage),
                (response, cursor) => viewModel.RbpAnalysis.Apply(response, cursor),
                client => new RbpReviewAnalysisAdapter(client)),
            new ReviewAlgorithmPanelRegistration(montageSelection, "stft", "STFT 时频分析", ReviewAnalysisPanelKind.Stft,
                viewModel.StftParameters, viewModel.StftAnalysis,
                (recording, algorithm, montage) => viewModel.StftParameters.Initialize(recording, algorithm, montage),
                (duration, rate, montage) => viewModel.StftParameters.BuildConfig(duration, rate, montage),
                (recording, previous, montage) => viewModel.StftParameters.RefreshInputOptions(recording, previous, montage),
                (response, _) => viewModel.StftAnalysis.Apply(response),
                client => new ReviewWindowAnalysisAdapter(client, "stft", "STFT")),
            new ReviewAlgorithmPanelRegistration(montageSelection, "tnk_biofeedback", "TNK 脑指标",
                ReviewAnalysisPanelKind.Tnk, viewModel.TnkParameters, viewModel.TnkAnalysis,
                (recording, algorithm, montage) =>
                {
                    viewModel.TnkParameters.Initialize(recording, algorithm, montage);
                    viewModel.TnkAnalysis.Initialize(algorithm);
                    viewModel.TnkAnalysis.SetVisibleStart(viewModel.AnalysisPanelStartSeconds);
                },
                (duration, rate, montage) => viewModel.TnkParameters.BuildConfig(duration, rate, montage),
                (recording, _, montage) => viewModel.TnkParameters.RefreshInputOptions(recording, montage),
                (response, _) => viewModel.TnkAnalysis.Apply(response, viewModel.AnalysisPanelStartSeconds),
                client => new ReviewTnkAnalysisAdapter(client)),
            Scalar("额叶 Alpha 不对称性", "FAA", viewModel.FaaParameters, viewModel.FaaAnalysis, montageSelection),
            Scalar("个体 Alpha 峰频率", "IAPF", viewModel.IapfParameters, viewModel.IapfAnalysis, montageSelection),
            Scalar("Theta/Beta 比值", "Theta/Beta", viewModel.ThetaBetaParameters, viewModel.ThetaBetaAnalysis, montageSelection),
            Scalar("经典 BrainBeat（绝对功率）", "脑节律", viewModel.BrainbeatParameters, viewModel.BrainbeatAnalysis,
                montageSelection),
            Scalar("个体化 BrainBeat（IAPF 相对功率）", "个体化 BrainBeat",
                viewModel.IndividualizedBrainbeatParameters, viewModel.BrainbeatAnalysis, montageSelection),
        };
        return registrations.ToDictionary(item => item.AlgorithmId, StringComparer.Ordinal);
    }

    private static ReviewAlgorithmPanelRegistration Scalar(string title, string adapterName,
        ReviewScalarParameters parameters, ReviewScalarAnalysisViewModel presentation,
        WorkspaceMontageSelection<MontageProfile> montageSelection) =>
        new(montageSelection, parameters.AlgorithmId, title, ReviewAnalysisPanelKind.Scalar, parameters, presentation,
            (recording, algorithm, montage) => parameters.Initialize(recording, algorithm, montage),
            (duration, rate, montage) => parameters.BuildConfig(duration, rate, montage),
            (recording, previous, montage) => parameters.RefreshInputOptions(recording, previous, montage),
            (response, _) => presentation.Apply(response, parameters.Channel, parameters.SecondaryChannel),
            client => new ReviewWindowAnalysisAdapter(client, parameters.AlgorithmId, adapterName));
}
