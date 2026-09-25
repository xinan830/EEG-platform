from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmMode


class PsdConfig(AlgorithmConfigBase):
    """Static single-channel PSD configuration."""

    mode: AlgorithmMode = "static"
