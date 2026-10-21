from app.algorithm_runtime.contracts import AlgorithmManifest
from app.algorithms.capabilities import official_dynamic_capabilities, rbp_definition_graph
from app.algorithms.rbp.official import RBP_BANDS
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="rbp",
    display_name_zh="相对频段功率",
    abbreviation="RBP",
    purpose_zh="展示所选通道 Delta、Theta、Alpha、Beta、Gamma 在 1–50 Hz 五频段总功率中的相对占比。",
    scientific_version="official-rbp-v2",
    is_current_version=True,
    implementation_identity="rbp-runtime-v2-five-band",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "multi_metric_series",
                   "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {name: [1] for name in ("time_s", "delta", "theta", "alpha", "beta", "gamma")},
                   "fields": [
        {"name": "delta", "unit": "ratio", "meaning": "Delta 相对功率"},
        {"name": "theta", "unit": "ratio", "meaning": "Theta 相对功率"},
        {"name": "alpha", "unit": "ratio", "meaning": "Alpha 相对功率"},
        {"name": "beta", "unit": "ratio", "meaning": "Beta 相对功率"},
        {"name": "gamma", "unit": "ratio", "meaning": "Gamma 相对功率"},
    ]},
    definition_persistence={
        "name": "Official RBP", "version": "2.0.1",
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
