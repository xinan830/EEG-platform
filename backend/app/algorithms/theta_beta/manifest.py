from app.algorithm_runtime.contracts import AlgorithmManifest, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


MANIFEST = AlgorithmManifest(
    algorithm_id="theta_beta",
    display_name_zh="Theta/Beta 比值",
    abbreviation="Theta/Beta",
    purpose_zh="根据同一选定通道的个体 Alpha 峰，计算 Theta 功率与 Beta 功率的比值。",
    scientific_version="official-theta-beta-v2",
    is_current_version=True,
    implementation_identity="theta-beta-runtime-v1",
    supported_modes=["static", "dynamic"],
    signal_requirements=SignalRequirements(input_roles=["channel"], representation="spectrum", source_shape="single", preprocessing_scope="module_defined"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=True,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "scalar_series", "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "theta_beta": [1]},
                   "fields": [{"name": "theta_beta", "unit": "dimensionless", "meaning": "个体化 Theta 功率除以个体化 Beta 功率"}]},
    definition_persistence={
        "name": "Official THETA_BETA", "version": "1.0.0",
        "graph": official_result_graph(["theta_beta"]),
        "definition_metadata": {"inputs": ["selected_raw_channel", "IAPF"], "formula": "theta(iapf-6..iapf-2) / beta(iapf+2..30)", "channels": ["selected_raw_channel"], "quality": ANALYSIS_CONTRACT["quality_gate_policy"]},
        "inputs": {"raw_channel": {"type": "TimeSeries", "unit": "V", "channel_order": "selected"}},
        "outputs": {"theta_beta": {"type": "Scalar", "unit": "dimensionless"}},
        "quality_rules": {"contract": ANALYSIS_CONTRACT["quality_gate_policy"]},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="official_composite_run_adapter",
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="scalar", dynamic_result_type="series",
    evidence_fields=["theta_beta_evidence", "iapf_hz", "theta_range_hz", "beta_range_hz"],
)
