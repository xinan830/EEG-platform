from app.algorithm_runtime.contracts import AlgorithmManifest, DynamicAnalysisPolicy, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, rbp_definition_graph
from app.algorithms.rbp.official import RBP_BANDS
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="rbp",
    display_name_zh="相对频段功率",
    abbreviation="RBP",
    purpose_zh="按所选导联输出 Delta、Theta、Alpha、Beta、Gamma 的相对占比与绝对功率。",
    scientific_version="official-rbp-v4",
    is_current_version=True,
    implementation_identity="rbp-runtime-v7-configurable-notch",
    supported_modes=["static", "dynamic"],
    dynamic_policy=DynamicAnalysisPolicy(default_window_s=10.0),
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrum", source_shape="channel_set", preprocessing_scope="fixed_continuous_before_slice"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "algorithm_group",
                   "window_axes": [{"name": "time_s", "unit": "s"}, {"name": "channel_index", "unit": "index"}],
                   "window_shapes": {"time_s": ["window"], "channel_index": ["channel"], **{name: ["channel"] for band, *_ in RBP_BANDS for name in (band, f"{band}_absolute_power")}},
                   "static_shapes": {"time_s": [1], "channel_index": ["channel"], **{name: ["channel"] for band, *_ in RBP_BANDS for name in (band, f"{band}_absolute_power")}},
                   "on_demand_shapes": {"time_s": [1], "channel_index": ["channel"], **{name: ["channel"] for band, *_ in RBP_BANDS for name in (band, f"{band}_absolute_power")}},
                   "fields": [
        {"name": "time_s", "unit": "s", "meaning": "窗口结束时间"},
        {"name": "channel_index", "unit": "index", "meaning": "导联顺序索引", "shape": ["channel"]},
        {"name": "delta", "unit": "ratio", "meaning": "Delta 相对功率", "shape": ["channel"]},
        {"name": "theta", "unit": "ratio", "meaning": "Theta 相对功率", "shape": ["channel"]},
        {"name": "alpha", "unit": "ratio", "meaning": "Alpha 相对功率", "shape": ["channel"]},
        {"name": "beta", "unit": "ratio", "meaning": "Beta 相对功率", "shape": ["channel"]},
        {"name": "gamma", "unit": "ratio", "meaning": "Gamma 相对功率", "shape": ["channel"]},
        *[{"name": f"{band}_absolute_power", "unit": "uV^2", "meaning": f"{band.title()} 绝对功率", "shape": ["channel"]}
          for band, *_ in RBP_BANDS],
    ]},
    definition_persistence={
        "name": "Official RBP", "version": "2.0.6",
        "definition_metadata": {
            "inputs": ["PSD"],
            "formula": "band_power / sum(five_band_powers_1_50)",
            "bands": {name: [low, high] for name, low, high in RBP_BANDS},
            "preprocessing": {"reference": ANALYSIS_CONTRACT["reference"], "bandpass_hz": ANALYSIS_CONTRACT["bandpass_hz"], "notch_hz": ANALYSIS_CONTRACT["notch_hz"], "notch_quality_factor": ANALYSIS_CONTRACT["notch_quality_factor"]},
            "welch": {"segment_s": ANALYSIS_CONTRACT["welch_segment_s"], "overlap": ANALYSIS_CONTRACT["welch_segment_overlap"], "window": ANALYSIS_CONTRACT["welch_window"]},
            "quality": "per_channel_annotation",
            "artifact_policy": "retain_finite_segments_with_warnings",
            "hard_rejections": ["missing_samples", "non_finite", "invalid_total_power"],
        },
        "graph": rbp_definition_graph(),
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {
            **{name: {"type": "RelativePower", "unit": "ratio"} for name, *_ in RBP_BANDS},
            **{f"{name}_absolute_power": {"type": "BandPower", "unit": "uV^2"} for name, *_ in RBP_BANDS},
        },
        "quality_rules": {"policy": "per_channel_annotation", "execution_kind": "generic_research_primitives"},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="structured", dynamic_result_type="structured_series",
    evidence_fields=["bands", "window_s", "denominator", "band_powers"],
)
