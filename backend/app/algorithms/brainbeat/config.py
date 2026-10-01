from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


class BrainbeatConfig(AlgorithmConfigBase):
    """Explicit Fz/Pz source channels for independent-window Brainbeat."""

    pz_channel: str = Field(min_length=1, validation_alias="secondary_channel")

    @model_validator(mode="after")
    def validate_pair(self) -> "BrainbeatConfig":
        if self.channel.casefold() == self.pz_channel.casefold():
            raise ValueError("Brainbeat Fz and Pz source channels must be different")
        if self.channel.casefold() != "fz" or self.pz_channel.casefold() != "pz":
            raise ValueError("Brainbeat requires Fz as the primary channel and Pz as the secondary channel")
        return self
