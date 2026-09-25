from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="band_ratio",
    display_name_zh="频段功率比",
    abbreviation="Band Ratio",
    purpose_zh="计算用户指定的两个频段功率之比。",
    scientific_version="official-band-ratio-v1",
    implementation_identity="band-ratio-runtime-v1",
    supported_modes=["static", "dynamic"],
    output_schema={"fields": [{"name": "ratio", "unit": "ratio", "meaning": "分子频段功率除以分母频段功率"}]},
    definition_name="Official Band Ratio",
    execution_kind="generic_research_primitives",
)
