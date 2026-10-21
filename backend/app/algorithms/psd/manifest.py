from app.algorithm_runtime.contracts import AlgorithmManifest
from app.algorithms.capabilities import official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="psd",
    display_name_zh="功率谱密度",
    abbreviation="PSD",
    purpose_zh="使用冻结的 Welch 契约计算所选原始通道的功率谱密度。",
    scientific_version="offline-spectral-v3",
    is_current_version=True,
    implementation_identity="psd-runtime-v3-explicit-filters",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
        on_demand_to_full=True, inverse_reuse_evidence=["backend/tests/test_official_directional_reuse.py"],
    ),
    output_schema={"mode_shapes": {"static": "frequency", "dynamic": "window x frequency"},
        "window_output_kind": "frequency_series",
        "window_shapes": {"frequency_hz": ["frequency"], "psd": ["frequency"], "band_share": [5]}, "fields": [
        {"name": "frequency_hz", "unit": "Hz", "shape": "frequency", "meaning": "PSD 频率轴"},
        {"name": "psd", "unit": "V^2/Hz", "shape": "frequency", "meaning": "所选通道的功率谱密度"},
        {"name": "band_share", "unit": "ratio", "shape": "5 (static) or window x 5 (dynamic)", "meaning": "Delta、Theta、Alpha、Beta、Gamma 的频段功率占比"},
    ]},
    definition_persistence={
        "name": "Official PSD", "version": "1.0.0",
        "graph": official_result_graph(["frequency_hz", "psd", "band_share"]),
        "inputs": {"raw_channel": {"type": "TimeSeries", "unit": "V", "channel_order": "selected"}},
        "outputs": {"frequency_hz": {"type": "FrequencyAxis", "unit": "Hz"}, "psd": {"type": "PowerSpectralDensity", "unit": "V^2/Hz"}, "band_share": {"type": "RelativePower", "unit": "ratio"}},
        "quality_rules": {"official_result": True},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)
