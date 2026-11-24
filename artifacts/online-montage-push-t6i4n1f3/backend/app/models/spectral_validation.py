"""Read-only requests for engineering-only spectral reference checks."""

from pydantic import BaseModel, Field, model_validator


class SpectralReferenceValidationRequest(BaseModel):
    start_s: float = Field(ge=0.0)
    end_s: float = Field(gt=0.0)
    channels: list[str] = Field(min_length=1)

    @model_validator(mode="after")
    def validate_range_and_channels(self):
        if self.end_s <= self.start_s:
            raise ValueError("end_s must be greater than start_s")
        if any(not item.strip() for item in self.channels):
            raise ValueError("channels must not contain empty labels")
        if len({item.casefold() for item in self.channels}) != len(self.channels):
            raise ValueError("channels must not contain duplicates")
        return self
