from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase, AlgorithmMode


class PsdConfig(AlgorithmConfigBase):
    """Static single-channel PSD configuration."""

    mode: AlgorithmMode = "static"
    low_hz: float = Field(default=1.0, gt=0.0)
    high_hz: float = Field(default=30.0, gt=0.0)
    notch_hz: float | None = Field(default=50.0)

    @model_validator(mode="after")
    def validate_frequency_range(self) -> "PsdConfig":
        if self.low_hz <= 0.0 or self.high_hz <= self.low_hz:
            raise ValueError("PSD frequency range must satisfy high_hz > low_hz")
        if self.notch_hz == 0:
            self.notch_hz = None
        if self.notch_hz is not None and self.notch_hz not in (50.0, 60.0):
            raise ValueError("PSD notch_hz must be null, 50, or 60")
        return self
