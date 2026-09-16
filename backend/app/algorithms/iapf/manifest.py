from app.algorithm_runtime.contracts import AlgorithmManifest


MANIFEST = AlgorithmManifest(
    algorithm_id="iapf",
    display_name_zh="个体 Alpha 峰频率",
    abbreviation="IAPF",
    purpose_zh="从选定通道的 Alpha 残差中估计个体 Alpha 峰频率。",
    scientific_version="official-iapf-v2",
    # IAPF owns its peak/COG mathematics, but shares the platform's dynamic
    # frame schedule with every other playback metric.
    implementation_identity="iapf-runtime-v3",
    supported_modes=["static", "dynamic"],
    output_unit="Hz",
)
