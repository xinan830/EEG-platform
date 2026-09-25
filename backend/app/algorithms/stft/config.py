from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmMode


class StftConfig(AlgorithmConfigBase):
    """Static single-channel spectrogram configuration."""

    mode: AlgorithmMode = "static"
