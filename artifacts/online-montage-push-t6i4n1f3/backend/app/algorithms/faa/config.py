from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


class FaaConfig(AlgorithmConfigBase):
    """Explicit F3/F4 raw sources for the paired FAA calculation."""

    f4_channel: str = Field(min_length=1)
    low_hz: float = Field(default=1.0, gt=0)
    high_hz: float = Field(default=50.0, gt=0)
    notch_hz: float | None = Field(default=50.0)

    @model_validator(mode="after")
    def validate_pair(self) -> "FaaConfig":
        if self.channel.casefold() == self.f4_channel.casefold():
            raise ValueError("FAA F3 and F4 source channels must be different")
        if self.high_hz <= self.low_hz:
            raise ValueError("FAA filter range must satisfy high_hz > low_hz")
        if self.notch_hz is not None and self.notch_hz not in (50.0, 60.0):
            raise ValueError("FAA notch_hz must be null, 50, or 60")
        return self
