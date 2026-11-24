from app.algorithm_runtime.contracts import AlgorithmConfigBase
from pydantic import Field
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


class BandRatioConfig(AlgorithmConfigBase):
    numerator_low_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)
    numerator_high_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)
    denominator_low_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)
    denominator_high_hz: float = Field(ge=ANALYSIS_CONTRACT["bandpass_hz"][0], le=ANALYSIS_CONTRACT["bandpass_hz"][1], allow_inf_nan=False)

    def model_post_init(self, __context) -> None:
        if self.numerator_high_hz <= self.numerator_low_hz:
            raise ValueError("numerator band must satisfy high_hz > low_hz")
        if self.denominator_high_hz <= self.denominator_low_hz:
            raise ValueError("denominator band must satisfy high_hz > low_hz")
