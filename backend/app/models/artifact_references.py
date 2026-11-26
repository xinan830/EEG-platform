"""Typed source-voltage discovery contract for artifact reference selection."""
from pydantic import BaseModel


class ArtifactReferenceChannel(BaseModel):
    name: str
    unit: str = "V"


class ArtifactReferenceCatalogue(BaseModel):
    recording_id: str
    sampling_rate_hz: float
    sample_count: int
    channels: list[ArtifactReferenceChannel]


class PreprocessingAnalysisChannels(BaseModel):
    recording_id: str
    channels: list[str]
