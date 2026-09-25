from app.algorithm_runtime.contracts import AlgorithmConfigBase


class PsdConfig(AlgorithmConfigBase):
    """Static single-channel PSD configuration."""

    mode: str = "static"

