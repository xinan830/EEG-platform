from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="peak_frequency",
    display_name_zh="频段峰频率",
    abbreviation="Peak Frequency",
    purpose_zh="在用户指定频段内，从 PSD 频率网格选择峰值频率。",
    scientific_version="official-peak-frequency-v1",
    implementation_identity="peak-frequency-runtime-v1",
    supported_modes=["static", "dynamic"],
    output_schema={"fields": [{"name": "peak_frequency_hz", "unit": "Hz", "meaning": "最大 PSD 对应的频率"}]},
    definition_name="Official Peak Frequency",
    execution_kind="generic_research_primitives",
)
