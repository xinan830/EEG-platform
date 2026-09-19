from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="rbp",
    display_name_zh="相对频段功率",
    abbreviation="RBP",
    purpose_zh="展示所选通道 Delta、Theta、Alpha、Beta 在 1–30 Hz 总功率中的相对占比。",
    scientific_version="offline-spectral-v3",
    implementation_identity="rbp-runtime-v1",
    supported_modes=["static"],
    output_unit="ratio",
    definition_name="Official RBP",
    execution_kind="generic_research_primitives",
)
