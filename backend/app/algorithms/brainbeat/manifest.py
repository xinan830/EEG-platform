from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="brainbeat",
    display_name_zh="脑节律指标",
    abbreviation="BrainBeat",
    purpose_zh="根据 Fz 相对 Theta 与 Pz 相对 Alpha 的关系计算脑节律指标。",
    scientific_version="official-brainbeat-v1",
    implementation_identity="brainbeat-runtime-v1",
    supported_modes=["static", "dynamic"],
    output_schema={"fields": [{"name": "brainbeat", "unit": "ratio", "meaning": "Fz 相对 Theta 除以 Pz 相对 Alpha"}]},
    definition_name="Official BRAINBEAT",
    execution_kind="official_composite_run_adapter",
    availability="available",
    is_runnable=True,
    required_channel_roles=["Fz", "Pz", "IAPF"],
)
