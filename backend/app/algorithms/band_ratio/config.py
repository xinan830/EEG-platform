from app.algorithm_runtime.contracts import AlgorithmConfigBase


class BandRatioConfig(AlgorithmConfigBase):
    numerator_low_hz: float
    numerator_high_hz: float
    denominator_low_hz: float
    denominator_high_hz: float

    def model_post_init(self, __context) -> None:
        if self.numerator_high_hz <= self.numerator_low_hz:
            raise ValueError("numerator band must satisfy high_hz > low_hz")
        if self.denominator_high_hz <= self.denominator_low_hz:
            raise ValueError("denominator band must satisfy high_hz > low_hz")
