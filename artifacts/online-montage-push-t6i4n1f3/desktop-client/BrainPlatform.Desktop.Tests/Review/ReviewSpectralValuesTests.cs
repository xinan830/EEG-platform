using BrainPlatform.Desktop.Modules.Review.Analysis;
using static BrainPlatform.Desktop.Tests.Review.ReviewSpectralFixture;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewSpectralValuesTests
{
    [Fact]
    public void Updates_band_changes_and_unavailable_states_preserve_rows_and_notify_only_changed_properties()
    {
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);
        var rows = model.Rows.ToArray();
        var collectionEvents = 0;
        var properties = new List<string?>();
        model.Rows.CollectionChanged += (_, _) => collectionEvents++;
        rows[0].PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        var psd = Window("psd");
        var rbp = Window("rbp");

        model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
        Assert.Contains(nameof(ReviewSpectralValueRow.PsdDensityUv2Hz), properties);
        properties.Clear();
        model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
        Assert.Empty(properties);
        model.SelectedBandId = "theta";
        Assert.Equal(2, rows[0].PsdDensityUv2Hz);
        model.Clear("等待窗口");
        Assert.Null(rows[0].PsdDensityUv2Hz);
        model.InitializeChannels(ChannelNames);

        Assert.Equal(0, collectionEvents);
        for (var index = 0; index < rows.Length; index++) Assert.Same(rows[index], model.Rows[index]);
        model.InitializeChannels(["F3-AVG"]);
        Assert.Equal("F3-AVG", Assert.Single(model.Rows).Channel);
        Assert.NotSame(rows[0], model.Rows[0]);
    }

    [Fact]
    public void Table_uses_each_source_value_and_only_formats_declared_units()
    {
        var psd = Window("psd");
        var rbp = Window("rbp");
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);

        model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));

        Assert.Equal(ChannelNames, model.Rows.Select(row => row.Channel));
        var first = model.Rows[0];
        Assert.Equal(2, first.AmplitudeUv);
        Assert.Equal(4, first.AbsolutePowerUv2);
        Assert.Equal(25, first.RelativePowerPercent);
        Assert.Equal(3, first.PsdDensityUv2Hz); // PSD deliberately differs from RBP / bandwidth.
        Assert.Contains("clean", model.StatusText);
        Assert.Contains("样本 [5000, 10000)", model.WindowText);
        Assert.Contains("psd-10000", model.ProvenanceText);

        model.SelectedBandId = "gamma";
        Assert.Equal(5, model.Rows[0].PsdDensityUv2Hz);
        model.Clear("窗口已改变");
        Assert.All(model.Rows, row => Assert.Null(row.PsdDensityUv2Hz));
        Assert.Equal(ChannelNames, model.Rows.Select(row => row.Channel));
    }

    [Theory]
    [InlineData("clean", "正常（clean）")]
    [InlineData("warning", "警告（warning）")]
    [InlineData("unavailable", "不可用（unavailable）")]
    [InlineData("unknown", "未知（unknown）")]
    public void Row_details_summarize_quality_without_dumping_nested_evidence(string status, string label)
    {
        var psd = Window("psd");
        var rbp = Window("rbp") with { Evidence = Json(new { extensions = new
        {
            channel_order = new { channels = ChannelNames },
            channel_quality = new { by_channel = ChannelNames.ToDictionary(channel => channel, _ => new
            {
                status, reasons = new[] { "some_unavailable_epochs" }, clean_segments = 3, total_segments = 4,
                peak_uv = 30.829810007080464, clean_ratio = 0.75,
                evidence = new { schema_version = "spectral-window-evidence-v1", welch = new { segment_s = 4 } },
            }) },
        }}) };
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);

        model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));

        var row = model.Rows[0];
        Assert.Contains("F3 · Alpha 波 · 8–13 Hz", row.Details);
        Assert.Contains($"RBP：{label}", row.Details);
        Assert.Contains("some_unavailable_epochs", row.Details);
        Assert.Contains("有效分段：3/4", row.Details);
        Assert.Contains("峰值：30.83 µV", row.Details);
        Assert.DoesNotContain("{", row.Details);
        Assert.DoesNotContain("schema_version", row.Details);
        Assert.Equal(status is "clean" or "warning" ? 4d : (double?)null, row.AbsolutePowerUv2);
        Assert.Equal(3, row.PsdDensityUv2Hz);
    }

    [Fact]
    public void Row_details_format_display_units_and_keep_scalar_precision_and_failure_code()
    {
        var psd = Window("psd");
        var rbp = Window("rbp");
        var density = Projection(psd, "psd");
        var fields = density.Fields.ToArray();
        fields[2] = fields[2] with { Points = [fields[2].Points[0] with { Value = 4.2874959472903301e-12 }, fields[2].Points[1]] };
        var amplitude = Projection(rbp, "rbp");
        var amplitudes = amplitude.Fields.ToArray();
        amplitudes[2] = amplitudes[2] with
        {
            Points = amplitudes[2].Points.Select(point => point with { Value = null, QualityStatus = "unavailable" }).ToArray(),
            Failure = Json(new { code = "ABSOLUTE_POWER_NOT_AVAILABLE", message = "缺少绝对功率", details = new { raw = "hidden" } }),
        };
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);

        model.Apply(psd, rbp, density with { Fields = fields }, amplitude with { Fields = amplitudes });

        Assert.Equal(4.2874959472903301e-12 * 1e12, model.Rows[0].PsdDensityUv2Hz);
        Assert.Contains("频段平均 PSD：4.2875 µV²/Hz", model.Rows[0].Details);
        Assert.Contains("相对功率：25 %", model.Rows[0].Details);
        Assert.Contains("ABSOLUTE_POWER_NOT_AVAILABLE：缺少绝对功率", model.Rows[0].Details);
        Assert.Null(model.Rows[0].AmplitudeUv);
        Assert.DoesNotContain("{", model.Rows[0].Details);
        Assert.DoesNotContain("hidden", model.Rows[0].Details);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    public void Invalid_rbp_quality_cannot_be_presented_as_valid_power_or_amplitude(string quality)
    {
        var psd = Window("psd");
        var rbp = Window("rbp", quality: quality);
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);
        model.Apply(psd, rbp, Projection(psd, "psd"), Projection(rbp, "rbp"));
        Assert.All(model.Rows, row =>
        {
            Assert.Null(row.AmplitudeUv);
            Assert.Null(row.AbsolutePowerUv2);
            Assert.Null(row.RelativePowerPercent);
            Assert.NotNull(row.PsdDensityUv2Hz);
        });
    }

    [Fact]
    public void Quality_uses_status_instead_of_searching_reason_text_and_missing_values_stay_null()
    {
        var psd = Window("psd");
        var rbp = Window("rbp") with { Evidence = Json(new { extensions = new
        {
            channel_order = new { channels = ChannelNames }, channel_quality = new { by_channel = ChannelNames.ToDictionary(
                channel => channel, _ => new { status = "warning", reasons = new[] { "some_unavailable_epochs" } }) },
        }}) };
        var arrays = rbp.Arrays!.ToDictionary();
        arrays["alpha_absolute_power"] = Json(new double?[] { null, 0 });
        rbp = rbp with { Arrays = arrays };
        var density = Projection(psd, "psd");
        var fields = density.Fields.ToArray();
        var alpha = fields[2];
        fields[2] = alpha with { Points = [alpha.Points[0] with { QualityStatus = "unavailable" }, alpha.Points[1]] };
        var model = new ReviewSpectralValuesViewModel();
        model.InitializeChannels(ChannelNames);

        model.Apply(psd, rbp, density with { Fields = fields }, Projection(rbp, "rbp"));

        Assert.Null(model.Rows[0].AbsolutePowerUv2);
        Assert.Null(model.Rows[0].AmplitudeUv);
        Assert.Null(model.Rows[0].PsdDensityUv2Hz);
        Assert.Equal(25, model.Rows[0].RelativePowerPercent);
        Assert.Equal(0, model.Rows[1].AbsolutePowerUv2);
    }

    [Theory]
    [InlineData("cursor")]
    [InlineData("window")]
    [InlineData("channels")]
    [InlineData("filter")]
    [InlineData("welch")]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("evidence")]
    public void Source_pair_rejects_mismatched_identity_or_analysis_settings(string mismatch)
    {
        var psd = Window("psd");
        var rbp = mismatch switch
        {
            "cursor" => Window("rbp", cursor: 10500),
            "channels" => Window("rbp", channels: ["F7", "F3"]),
            "filter" => Window("rbp", filter: "different"),
            "welch" => Window("rbp", overlap: 0.75),
            "source" => Window("rbp", source: "other"),
            "digest" => Window("rbp") with { ScientificConfigurationJson = "{}" },
            "evidence" => Window("rbp") with { Evidence = null },
            _ => Window("rbp"),
        };
        if (mismatch == "window") rbp = rbp with { Window = rbp.Window! with { StartSample = 5001 } };
        Assert.Throws<InvalidOperationException>(() => ReviewSpectralSources.Validate(psd, rbp, ChannelNames));
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("window")]
    [InlineData("channels")]
    [InlineData("version")]
    [InlineData("algorithm")]
    [InlineData("status")]
    public void Projection_rejects_wrong_unit_window_and_channel_order(string mismatch)
    {
        var psd = Window("psd");
        var response = Projection(psd, "psd", mismatch == "channels" ? ["F7", "F3"] : null);
        if (mismatch == "window") response = response with { WindowId = "old-window" };
        if (mismatch == "version") response = response with { AlgorithmVersion = "wrong-version" };
        if (mismatch == "algorithm") response = response with { AlgorithmId = "rbp" };
        if (mismatch == "status") response = response with { Status = "complete" };
        if (mismatch == "unit") response = response with
            { Fields = response.Fields.Select(field => field with { Unit = "uV^2" }).ToArray() };
        Assert.Throws<InvalidOperationException>(() => ReviewSpectralSources.ValidateProjection(
            response, psd, ChannelNames, "psd.band-mean-density.", "V^2/Hz"));
    }
}
