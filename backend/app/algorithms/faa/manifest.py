from app.algorithm_runtime.contracts import AlgorithmManifest, DynamicAnalysisPolicy
from app.algorithms.capabilities import official_dynamic_capabilities, official_result_graph


MANIFEST = AlgorithmManifest(
    algorithm_id="faa",
    display_name_zh="额叶 Alpha 不对称性",
    abbreviation="FAA",
    purpose_zh="比较明确选择的 F3 与 F4 来源通道的 Alpha 功率对数差。",
    scientific_version="official-faa-v2",
    is_current_version=True,
    implementation_identity="faa-runtime-v2",
    supported_modes=["static", "dynamic"],
    dynamic_capabilities=official_dynamic_capabilities(full_to_preview=False),
    dynamic_policy=DynamicAnalysisPolicy(minimum_window_s=11.0, window_options_s=[20.0, 30.0], default_window_s=20.0, refresh_step_s=1.0, allow_warmup=False),
    output_schema={"window_output_kind": "scalar_series",
                   "window_axes": [{"name": "time_s", "unit": "s"}],
                   "window_shapes": {"time_s": [1], "faa": [1]},
                   "fields": [{"name": "faa", "unit": "dimensionless", "meaning": "F4 Alpha 功率对数减 F3 Alpha 功率对数"}]},
    definition_persistence={
        "name": "Official FAA", "version": "2.0.0",
        "graph": official_result_graph(["faa"]),
        "definition_metadata": {"inputs": ["F3", "F4"], "formula": "ln(alpha_power_F4) - ln(alpha_power_F3)", "epoch_s": 2.0, "overlap": 0.5, "paired_quality": True, "minimum_clean_epochs": 10, "preprocessing_scope": "whole_recording_before_window_slice"},
        "inputs": {"F3": {"type": "TimeSeries", "unit": "V"}, "F4": {"type": "TimeSeries", "unit": "V"}},
        "outputs": {"faa": {"type": "Scalar", "unit": "dimensionless"}},
        "quality_rules": {"paired_epochs": True, "minimum_clean_epochs": 10},
        "references": ["docs/algorithms/04-spectral-metrics.md"],
    },
    execution_kind="official_composite_run_adapter",
)
