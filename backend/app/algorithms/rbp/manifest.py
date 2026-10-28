from app.algorithm_runtime.contracts import AlgorithmManifest, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, rbp_definition_graph
from app.algorithms.rbp.official import RBP_BANDS
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="rbp",
    display_name_zh="相对频段功率",
    abbreviation="RBP",
    purpose_zh="按所选导联展示 Delta、Theta、Alpha、Beta、Gamma 在 1–50 Hz 五频段总功率中的相对占比。",
    scientific_version="official-rbp-v3",
    is_current_version=True,
    implementation_identity="rbp-runtime-v3-montage-multi-channel",
    supported_modes=["static", "dynamic"],
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrum", source_shape="single", preprocessing_scope="fixed_continuous_before_slice"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "algorithm_group",
                   "window_axes": [{"name": "time_s", "unit": "s"}, {"name": "channel_index", "unit": "index"}],
                   "window_shapes": {"time_s": ["window"], "channel_index": ["channel"], **{name: ["channel"] for name in ("delta", "theta", "alpha", "beta", "gamma")}},
                   "static_shapes": {"time_s": [1], "channel_index": ["channel"], **{name: ["channel"] for name in ("delta", "theta", "alpha", "beta", "gamma")}},
                   "on_demand_shapes": {"time_s": [1], "channel_index": ["channel"], **{name: ["channel"] for name in ("delta", "theta", "alpha", "beta", "gamma")}},
                   "fields": [
        {"name": "time_s", "unit": "s", "meaning": "窗口结束时间"},
        {"name": "channel_index", "unit": "index", "meaning": "导联顺序索引", "shape": ["channel"]},
        {"name": "delta", "unit": "ratio", "meaning": "Delta 相对功率", "shape": ["channel"]},
        {"name": "theta", "unit": "ratio", "meaning": "Theta 相对功率", "shape": ["channel"]},
        {"name": "alpha", "unit": "ratio", "meaning": "Alpha 相对功率", "shape": ["channel"]},
        {"name": "beta", "unit": "ratio", "meaning": "Beta 相对功率", "shape": ["channel"]},
        {"name": "gamma", "unit": "ratio", "meaning": "Gamma 相对功率", "shape": ["channel"]},
    ]},
    definition_persistence={
        "name": "Official RBP", "version": "2.0.2",
        "definition_metadata": {
            "inputs": ["PSD"],
            "formula": "band_power / sum(five_band_powers_1_50)",
            "bands": {name: [low, high] for name, low, high in RBP_BANDS},
            "preprocessing": {"reference": ANALYSIS_CONTRACT["reference"], "bandpass_hz": [1.0, 50.0], "notch_hz": 50.0, "notch_quality_factor": 30.0},
            "welch": {"segment_s": ANALYSIS_CONTRACT["welch_segment_s"], "overlap": ANALYSIS_CONTRACT["welch_segment_overlap"], "window": ANALYSIS_CONTRACT["welch_window"]},
            "quality": ANALYSIS_CONTRACT["quality_gate_policy"],
        },
        "graph": rbp_definition_graph(),
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {name: {"type": "RelativePower", "unit": "ratio"} for name, *_ in RBP_BANDS},
        "quality_rules": {"contract": "ANALYSIS_CONTRACT.quality_gate_policy", "execution_kind": "generic_research_primitives"},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="structured", dynamic_result_type="structured_series",
    evidence_fields=["bands", "window_s", "denominator", "band_powers"],
)
