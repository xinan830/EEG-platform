from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="theta_beta",
    display_name_zh="Theta/Beta 比值",
    abbreviation="Theta/Beta",
    purpose_zh="根据同一选定通道的个体 Alpha 峰，计算 Theta 功率与 Beta 功率的比值。",
    scientific_version="official-theta-beta-v2",
    implementation_identity="theta-beta-runtime-v1",
    supported_modes=["static", "dynamic"],
    output_schema={"fields": [{"name": "theta_beta", "unit": "ratio", "meaning": "个体化 Theta 功率除以个体化 Beta 功率"}]},
    definition_name="Official THETA_BETA",
    execution_kind="official_composite_run_adapter",
)
