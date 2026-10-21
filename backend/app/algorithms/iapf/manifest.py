from app.algorithm_runtime.contracts import AlgorithmManifest
from app.algorithms.capabilities import official_dynamic_capabilities, official_result_graph
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="iapf",
    display_name_zh="个体 Alpha 峰频率",
    abbreviation="IAPF",
    purpose_zh="从选定通道的 Alpha 残差中估计个体 Alpha 峰频率。",
    scientific_version="official-iapf-v2",
    is_current_version=True,
    # IAPF owns its peak/COG mathematics, but shares the platform's dynamic
    # frame schedule with every other playback metric.
    implementation_identity="iapf-runtime-v3",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "scalar_series", "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "iapf_hz": [1]},
                   "fields": [{"name": "iapf_hz", "unit": "Hz", "meaning": "个体 Alpha 峰频率"}]},
    definition_persistence={
        "name": "Official IAPF", "version": "1.0.0",
        "graph": official_result_graph(["iapf_hz"]),
        "definition_metadata": {
            "inputs": ["PSD"], "fit": ANALYSIS_CONTRACT["aperiodic_model"],
            "search_hz": ANALYSIS_CONTRACT["iapf_search_hz"],
            "sources": ["peak", "cog"], "lock_candidates": ANALYSIS_CONTRACT["iapf_lock_candidates"],
        },
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {"iapf_hz": {"type": "Scalar", "unit": "Hz"}},
        "quality_rules": {"contract": ANALYSIS_CONTRACT["quality_gate_policy"]},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="official_composite_run_adapter",
)
