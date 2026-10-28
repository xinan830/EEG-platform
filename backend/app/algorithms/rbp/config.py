from pydantic import Field, model_validator

from app.algorithm_runtime.contracts import AlgorithmConfigBase


class RbpConfig(AlgorithmConfigBase):
    """RBP channel selection and an absolute analysis range.

    ``channels`` is optional for the legacy single-channel request. When it
    contains more than one validated montage output, the module returns the
    channel-aware structured result family.
    """

    channels: list[str] = Field(default_factory=list, min_length=0)

    @model_validator(mode="after")
    def normalize_channels(self) -> "RbpConfig":
        selected = self.channels or [self.channel]
        normalized = []
        seen = set()
        for item in selected:
            value = item.strip()
            key = value.casefold()
            if not value or key in seen:
                raise ValueError("RBP channels must be non-empty and unique")
            normalized.append(value)
            seen.add(key)
        self.channels = normalized
        self.channel = normalized[0]
        return self
