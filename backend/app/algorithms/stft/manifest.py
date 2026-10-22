from app.algorithm_runtime.contracts import AlgorithmManifest, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="stft",
    display_name_zh="时频分析",
    abbreviation="STFT",
    purpose_zh="使用冻结的 spectrogram-v2 契约计算所选原始通道的时频功率。",
    scientific_version="spectrogram-v2",
    is_current_version=True,
    implementation_identity="stft-runtime-v4-relative-window-times",
    supported_modes=["static", "dynamic"],
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrogram", source_shape="single", preprocessing_scope="configured_continuous_before_slice"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
        on_demand_to_full=True, inverse_reuse_evidence=["backend/tests/test_official_directional_reuse.py"],
    ),
    output_schema={"mode_shapes": {"static": "time x frequency", "dynamic": "window x inner_time x frequency"},
        "window_output_kind": "time_frequency",
        "window_axes": [
            {"name": "time_center_s", "unit": "s", "shape": ["inner_time"]},
            {"name": "frequency_hz", "unit": "Hz", "shape": ["frequency"]},
        ],
        "window_shapes": {
            "time_center_s": ["inner_time"], "frequency_hz": ["frequency"],
            "power_linear": ["inner_time", "frequency"], "power_db": ["inner_time", "frequency"],
        },
        "fields": [
        {"name": "time_center_s", "unit": "s", "shape": "time", "meaning": "时频窗中心时间"},
        {"name": "frequency_hz", "unit": "Hz", "shape": "frequency", "meaning": "时频频率轴"},
        {"name": "power_linear", "unit": "V^2/Hz", "shape": "time x frequency", "meaning": "线性功率密度"},
        {"name": "power_db", "unit": "dB re 1 uV^2/Hz", "shape": "time x frequency", "meaning": "显示功率密度"},
    ]},
    definition_persistence={
        "name": "Official STFT", "version": "1.0.0",
        "graph": official_result_graph(["time_center_s", "frequency_hz", "power_linear", "power_db"]),
        "inputs": {"raw_channel": {"type": "TimeSeries", "unit": "V", "channel_order": "selected"}},
        "outputs": {"time_center_s": {"type": "TimeAxis", "unit": "s"}, "frequency_hz": {"type": "FrequencyAxis", "unit": "Hz"}, "power_linear": {"type": "PowerSpectralDensity", "unit": "V^2/Hz"}, "power_db": {"type": "PowerSpectralDensity", "unit": "dB re 1 uV^2/Hz"}},
        "quality_rules": {"official_result": True},
        "references": ["docs/algorithms/08-spectrogram.md"],
    },
    execution_kind="generic_research_primitives",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="structured", dynamic_result_type="structured_series",
    evidence_fields=["spectrogram_contract_version", "inner_valid_count", "padding_policy"],
)
