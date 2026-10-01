from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="rbp",
    display_name_zh="相对频段功率",
    abbreviation="RBP",
    purpose_zh="展示所选通道 Delta、Theta、Alpha、Beta、Gamma 在 1–50 Hz 五频段总功率中的相对占比。",
    scientific_version="official-rbp-v2",
    implementation_identity="rbp-runtime-v2-five-band",
    supported_modes=["static", "dynamic"],
    output_schema={"fields": [
        {"name": "delta", "unit": "ratio", "meaning": "Delta 相对功率"},
        {"name": "theta", "unit": "ratio", "meaning": "Theta 相对功率"},
        {"name": "alpha", "unit": "ratio", "meaning": "Alpha 相对功率"},
        {"name": "beta", "unit": "ratio", "meaning": "Beta 相对功率"},
        {"name": "gamma", "unit": "ratio", "meaning": "Gamma 相对功率"},
    ]},
    definition_name="Official RBP",
    execution_kind="generic_research_primitives",
)
