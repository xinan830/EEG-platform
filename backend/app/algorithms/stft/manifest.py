from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="stft",
    display_name_zh="时频分析",
    abbreviation="STFT",
    purpose_zh="使用冻结的 spectrogram-v2 契约计算所选原始通道的时频功率。",
    scientific_version="spectrogram-v2",
    implementation_identity="stft-runtime-v1",
    supported_modes=["static", "dynamic"],
    output_schema={"mode_shapes": {"static": "time x frequency", "dynamic": "window x inner_time x frequency"}, "fields": [
        {"name": "time_center_s", "unit": "s", "shape": "time", "meaning": "时频窗中心时间"},
        {"name": "frequency_hz", "unit": "Hz", "shape": "frequency", "meaning": "时频频率轴"},
        {"name": "power_linear", "unit": "V^2/Hz", "shape": "time x frequency", "meaning": "线性功率密度"},
        {"name": "power_db", "unit": "dB re 1 uV^2/Hz", "shape": "time x frequency", "meaning": "显示功率密度"},
    ]},
    definition_name="Official STFT",
    execution_kind="generic_research_primitives",
)
