from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


FZ_SOURCE_CHANNELS = frozenset({"fz", "fz-ref", "fz-avg"})
PZ_SOURCE_CHANNELS = frozenset({"pz", "pz-ref", "pz-avg"})


class BrainbeatConfig(AlgorithmConfigBase):
    """Explicit Fz/Pz source channels for independent-window Brainbeat."""

    pz_channel: str = Field(min_length=1, validation_alias="secondary_channel")

    @model_validator(mode="after")
    def validate_pair(self) -> "BrainbeatConfig":
        if self.channel.casefold() == self.pz_channel.casefold():
            raise ValueError("Brainbeat Fz and Pz source channels must be different")
        # REF and average-reference outputs preserve the named electrode role.
        # Bipolar outputs (e.g. Fz-Pz) do not represent the same Fz/Pz inputs.
        if self.channel.casefold() not in FZ_SOURCE_CHANNELS or self.pz_channel.casefold() not in PZ_SOURCE_CHANNELS:
            raise ValueError("Brainbeat requires Fz as the primary channel and Pz as the secondary channel")
        return self
