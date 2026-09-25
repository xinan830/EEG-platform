from app.algorithm_runtime.contracts import AlgorithmConfigBase


class PeakFrequencyConfig(AlgorithmConfigBase):
    """Generic peak-frequency configuration; it is not an IAPF contract."""

    low_hz: float
    high_hz: float

    def model_post_init(self, __context) -> None:
        if self.high_hz <= self.low_hz:
            raise ValueError("peak-frequency band must satisfy high_hz > low_hz")
