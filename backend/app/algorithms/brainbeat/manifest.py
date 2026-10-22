from app.algorithm_runtime.contracts import AlgorithmManifest, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="brainbeat",
    display_name_zh="脑节律指标",
    abbreviation="BrainBeat",
    purpose_zh="根据 Fz 相对 Theta 与 Pz 相对 Alpha 的关系计算脑节律指标。",
    scientific_version="official-brainbeat-v1",
    is_current_version=True,
    implementation_identity="brainbeat-runtime-v1",
    supported_modes=["static", "dynamic"],
    signal_requirements=SignalRequirements(input_roles=["Fz", "Pz"], representation="spectrum", source_shape="paired", preprocessing_scope="configured_continuous_before_slice"),
    dynamic_capabilities=official_dynamic_capabilities(
        full_to_preview=False,
        combined_evidence=["backend/tests/test_official_combined_strategies.py", "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs"],
    ),
    output_schema={"window_output_kind": "scalar_series",
                   "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "brainbeat": [1]},
                   "fields": [{"name": "brainbeat", "unit": "ratio", "meaning": "Fz 相对 Theta 除以 Pz 相对 Alpha"}]},
    definition_persistence={
        "name": "Official BRAINBEAT", "version": "1.0.0",
        "graph": official_result_graph(["brainbeat"]),
        "definition_metadata": {"inputs": ["Fz", "Pz", "IAPF"], "formula": "relative_theta_Fz / relative_alpha_Pz", "welch_segment_s": 2.0, "overlap": 0.5, "independent_windows": True, "cross_window_ema": False},
        "inputs": {"Fz": {"type": "TimeSeries", "unit": "V"}, "Pz": {"type": "TimeSeries", "unit": "V"}},
        "outputs": {"brainbeat": {"type": "Scalar", "unit": "ratio"}},
        "quality_rules": {"cross_window_ema": False},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="official_composite_run_adapter",
    availability="available",
    is_runnable=True,
    required_channel_roles=["Fz", "Pz", "IAPF"],
)

MANIFEST.output_contract = declared_output_contract(
    MANIFEST, static_result_type="scalar", dynamic_result_type="series",
    evidence_fields=["brainbeat_evidence", "paired_role_order", "window_source"],
)
