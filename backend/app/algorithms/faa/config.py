from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


class FaaConfig(AlgorithmConfigBase):
    """Explicit F3/F4 raw sources for the paired FAA calculation."""

    f4_channel: str = Field(min_length=1)

    @model_validator(mode="after")
    def validate_pair(self) -> "FaaConfig":
        if self.channel.casefold() == self.f4_channel.casefold():
            raise ValueError("FAA F3 and F4 source channels must be different")
        return self
