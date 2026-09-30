from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmMode


class StftConfig(AlgorithmConfigBase):
    """Static single-channel spectrogram configuration."""

    mode: AlgorithmMode = "static"
    low_hz: float = Field(default=1.0, gt=0)
    high_hz: float = Field(default=50.0, gt=0)
    notch_hz: float | None = None

    @model_validator(mode="after")
    def validate_frequency_contract(self) -> "StftConfig":
        if self.high_hz <= self.low_hz:
            raise ValueError("STFT frequency range must satisfy high_hz > low_hz")
        if self.notch_hz is not None and self.notch_hz not in (50.0, 60.0):
            raise ValueError("STFT notch_hz must be null, 50, or 60")
        return self
