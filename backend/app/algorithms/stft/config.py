from app.algorithm_runtime.contracts import AlgorithmConfigBase


class StftConfig(AlgorithmConfigBase):
    """Static single-channel spectrogram configuration."""

    mode: str = "static"
