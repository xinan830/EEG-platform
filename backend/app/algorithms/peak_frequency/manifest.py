from app.algorithm_runtime.contracts import AlgorithmManifest
from app.algorithms.capabilities import official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="peak_frequency",
    display_name_zh="频段峰频率",
    abbreviation="Peak Frequency",
    purpose_zh="在用户指定频段内，从 PSD 频率网格选择峰值频率。",
    scientific_version="official-peak-frequency-v1",
    is_current_version=True,
    implementation_identity="peak-frequency-runtime-v2-frequency-contract",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(full_to_preview=True),
    output_schema={"window_output_kind": "scalar_series", "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "peak_frequency_hz": [1]},
                   "fields": [{"name": "peak_frequency_hz", "unit": "Hz", "meaning": "最大 PSD 对应的频率"}]},
    definition_persistence={
        "name": "Official Peak Frequency", "version": "1.0.0",
        "graph": official_result_graph(["peak_frequency_hz"]),
        "definition_metadata": {"inputs": ["PSD"], "formula": "argmax(PSD within declared frequency band)", "edge_policy": "inclusive", "tie_policy": "lowest_frequency_grid_point", "interpolation": "none"},
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {"peak_frequency_hz": {"type": "Scalar", "unit": "Hz"}},
        "quality_rules": {"denominator_policy": "not_applicable"},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="generic_research_primitives",
)
