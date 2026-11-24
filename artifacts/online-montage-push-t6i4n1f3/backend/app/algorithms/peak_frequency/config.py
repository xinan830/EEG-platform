from app.algorithm_runtime.contracts import AlgorithmConfigBase
from pydantic import Field
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


class PeakFrequencyConfig(AlgorithmConfigBase):
    """Generic peak-frequency configuration; it is not an IAPF contract."""

    low_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)
    high_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)

    def model_post_init(self, __context) -> None:
        if self.high_hz <= self.low_hz:
            raise ValueError("peak-frequency band must satisfy high_hz > low_hz")
