from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="faa",
    display_name_zh="额叶 Alpha 不对称性",
    abbreviation="FAA",
    purpose_zh="比较明确选择的 F3 与 F4 来源通道的 Alpha 功率对数差。",
    scientific_version="official-faa-v1",
    implementation_identity="faa-runtime-v1",
    supported_modes=["static"],
    output_unit="dimensionless",
    definition_name="Official FAA",
    execution_kind="official_composite_run_adapter",
)
