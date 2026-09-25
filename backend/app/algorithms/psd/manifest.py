from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="psd",
    display_name_zh="功率谱密度",
    abbreviation="PSD",
    purpose_zh="使用冻结的 Welch 契约计算所选原始通道的功率谱密度。",
    scientific_version="offline-spectral-v3",
    implementation_identity="psd-runtime-v1",
    supported_modes=["static"],
    output_schema={"fields": [
        {"name": "frequency_hz", "unit": "Hz", "shape": "frequency", "meaning": "PSD 频率轴"},
        {"name": "psd", "unit": "V^2/Hz", "shape": "frequency", "meaning": "所选通道的功率谱密度"},
    ]},
    definition_name="Official PSD",
    execution_kind="generic_research_primitives",
)
