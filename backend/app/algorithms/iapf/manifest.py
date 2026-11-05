from app.algorithm_runtime.contracts import AlgorithmManifest, DynamicAnalysisPolicy, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="iapf",
    display_name_zh="个体 Alpha 峰频率",
    abbreviation="IAPF",
    purpose_zh="从选定通道的 Alpha 残差中估计个体 Alpha 峰频率。",
    scientific_version="official-iapf-v5",
    is_current_version=True,
    # Peak acceptance is IAPF-owned; its window policy is catalog-visible.
    implementation_identity="iapf-runtime-v7-20s-segment-diagnostics",
    supported_modes=["static", "dynamic"],
    dynamic_policy=DynamicAnalysisPolicy(
        minimum_window_s=20.0,
        window_options_s=[20.0, 30.0],
        default_window_s=20.0,
        refresh_step_s=1.0,
        refresh_step_options_s=[1.0, 2.0, 5.0],
        allow_warmup=False,
    ),
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrum", source_shape="single", preprocessing_scope="module_defined"),
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
            "peak_minimum_prominence_ratio": 0.20, "peak_minimum_width_bins": 1.2,
            "cog_minimum_excess_ratio": 0.20, "cog_minimum_effective_bins": 3.0,
        },
        "inputs": {"psd": {"type": "PSDSeries", "unit": "V^2/Hz", "channel_order": "preserved"}},
        "outputs": {"iapf_hz": {"type": "Scalar", "unit": "Hz"}},
        "quality_rules": {"contract": ANALYSIS_CONTRACT["quality_gate_policy"]},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="official_composite_run_adapter",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="scalar", dynamic_result_type="series",
    evidence_fields=[
        "iapf_evidence", "peak_hz", "cog_hz", "estimate_source",
        "peak_prominence_ratio", "peak_width_bins", "cog_excess_ratio", "cog_effective_bins",
    ],
)
