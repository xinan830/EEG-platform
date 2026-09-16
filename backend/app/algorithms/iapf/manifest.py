from app.algorithm_runtime.contracts import AlgorithmManifest, DynamicAnalysisPolicy


MANIFEST = AlgorithmManifest(
    algorithm_id="iapf",
    display_name_zh="个体 Alpha 峰频率",
    abbreviation="IAPF",
    purpose_zh="从选定通道的 Alpha 残差中估计个体 Alpha 峰频率。",
    scientific_version="official-iapf-v2",
    implementation_identity="iapf-runtime-v2",
    supported_modes=["static", "dynamic"],
    output_unit="Hz",
    dynamic_policy=DynamicAnalysisPolicy(
        minimum_window_s=30.0,
        window_options_s=[30.0],
        default_window_s=30.0,
        refresh_step_s=5.0,
        allow_warmup=False,
    ),
)
