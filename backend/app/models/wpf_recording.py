from pydantic import BaseModel, Field


class WpfRecordingRegistrationRequest(BaseModel):
    source_directory: str = Field(min_length=1)
