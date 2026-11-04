from app.algorithm_runtime.contracts import AlgorithmManifest, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="psd",
    display_name_zh="功率谱密度",
    abbreviation="PSD",
    purpose_zh="使用冻结的 Welch 契约计算所选原始通道的功率谱密度。",
    scientific_version="offline-spectral-v6-shared-1-50-notch",
    is_current_version=True,
    implementation_identity="psd-runtime-v6-shared-1-50-notch",
    supported_modes=["static", "dynamic"],
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrum", source_shape="channel_set", preprocessing_scope="configured_continuous_before_slice"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
        on_demand_to_full=True, inverse_reuse_evidence=["backend/tests/test_official_directional_reuse.py"],
    ),
    output_schema={"mode_shapes": {"static": "channel x frequency", "dynamic": "window x channel x frequency"},
        "window_output_kind": "frequency_series",
        "window_shapes": {"frequency_hz": ["frequency"], "psd": ["channel", "frequency"], "band_share": ["channel", 5]}, "fields": [
        {"name": "frequency_hz", "unit": "Hz", "shape": "frequency", "meaning": "PSD 频率轴"},
        {"name": "psd", "unit": "V^2/Hz", "shape": ["channel", "frequency"], "meaning": "按 channel_order 排列的功率谱密度"},
        {"name": "band_share", "unit": "ratio", "shape": ["channel", 5], "meaning": "按 channel_order 排列的 Delta、Theta、Alpha、Beta、Gamma 频段功率占比"},
    ]},
    definition_persistence={
        "name": "Official PSD", "version": "1.0.0",
        "graph": official_result_graph(["frequency_hz", "psd", "band_share"]),
        "inputs": {"raw_channels": {"type": "TimeSeries", "unit": "V", "channel_order": "explicit_ordered_set"}},
        "outputs": {"frequency_hz": {"type": "FrequencyAxis", "unit": "Hz"}, "psd": {"type": "PowerSpectralDensity", "unit": "V^2/Hz"}, "band_share": {"type": "RelativePower", "unit": "ratio"}},
        "quality_rules": {"official_result": True},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="structured", dynamic_result_type="structured_series",
    evidence_fields=["welch_parameters", "band_share", "sample_coordinate"],
)
