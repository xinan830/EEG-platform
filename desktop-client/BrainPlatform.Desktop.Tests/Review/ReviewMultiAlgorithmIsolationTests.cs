using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task RapidlyChangingTwoAlgorithmsUpdatesBothInsteadOfLeavingFirstStale()
    {
        var stft = new SpectralProvider("stft");
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(stft, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("stft");
        await vm.OpenAnalysisAsync("rbp");
        var stftSlot = vm.OpenAnalysisSlots.Single(slot => slot.AlgorithmId == "stft");
        var rbpSlot = vm.OpenAnalysisSlots.Single(slot => slot.AlgorithmId == "rbp");

        var initialStftConfigs = stft.Configurations.Count;
        var initialRbpConfigs = rbp.Configurations.Count;

        vm.StftParameters.Channel = "F7";
        await Task.Delay(80);
        vm.RbpParameters.Channel = "F7";

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline &&
               (stftSlot.IsStale || rbpSlot.IsStale ||
                stft.Configurations.Count <= initialStftConfigs ||
                rbp.Configurations.Count <= initialRbpConfigs))
        {
            await Task.Delay(20);
        }

        Assert.False(stftSlot.IsStale);
        Assert.False(rbpSlot.IsStale);
        Assert.True(stft.Configurations.Count > initialStftConfigs);
        Assert.True(rbp.Configurations.Count > initialRbpConfigs);
    }

    [Fact]
    public async Task OpeningSpectralValuesThenRbpUsesDistinctRbpCoordinatorsAndBothTrackCursor()
    {
        var psd = new SpectralProvider("psd");
        var rbp = new SpectralProvider("rbp");
        await using var vm = SpectralReview(psd, rbp);
        await vm.InitializeAsync();
        await vm.SeekAsync(20);
        await vm.OpenAnalysisAsync("spectral-values");
        await SpectralReady(vm, "psd-10000");

        await vm.OpenAnalysisAsync("rbp");
        var spectralSlot = vm.OpenAnalysisSlots.Single(slot => slot.AlgorithmId == "spectral-values");
        var rbpSlot = vm.OpenAnalysisSlots.Single(slot => slot.AlgorithmId == "rbp");

        Assert.NotSame(spectralSlot.SpectralRbpBinding!.Coordinator, rbpSlot.Coordinator);

        await vm.SeekAsync(30);
        await SpectralReady(vm, "psd-15000");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && rbpSlot.CurrentWindow?.Window?.WindowId != "rbp-15000")
            await Task.Delay(20);

        Assert.Equal("rbp-15000", rbpSlot.CurrentWindow?.Window?.WindowId);
        Assert.DoesNotContain("不匹配", vm.SpectralValues.ProvenanceText, StringComparison.Ordinal);
    }
}
