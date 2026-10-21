from pydantic import BaseModel, Field
from typing import Literal


class WpfRecordingRegistrationRequest(BaseModel):
    source_directory: str = Field(min_length=1)
    signal_mode: Literal["raw", "acquisition_montage"] = "raw"
    analysis_montage_profiles: list[dict[str, object]] = Field(default_factory=list)
