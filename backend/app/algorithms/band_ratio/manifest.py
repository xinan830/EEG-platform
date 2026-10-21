from app.algorithm_runtime.contracts import AlgorithmManifest
from app.algorithms.capabilities import official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="band_ratio",
    display_name_zh="频段功率比",
    abbreviation="Band Ratio",
    purpose_zh="计算用户指定的两个频段功率之比。",
    scientific_version="official-band-ratio-v1",
    is_current_version=True,
    implementation_identity="band-ratio-runtime-v2-frequency-contract",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "scalar_series", "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "ratio": [1]},
                   "fields": [{"name": "ratio", "unit": "ratio", "meaning": "分子频段功率除以分母频段功率"}]},
    definition_persistence={
        "name": "Official Band Ratio", "version": "1.0.0",
        "graph": official_result_graph(["band_ratio"]),
        "definition_metadata": {"inputs": ["band_power numerator", "band_power denominator"], "formula": "numerator_power / denominator_power", "denominator_policy": "strictly_positive"},
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {"band_ratio": {"type": "Scalar", "unit": "dimensionless"}},
        "quality_rules": {"denominator_policy": "strictly_positive"},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)
