"""Typed source-voltage discovery contract for artifact reference selection."""
from pydantic import BaseModel
from typing import Literal


class ArtifactReferenceChannel(BaseModel):
    name: str
    unit: str = "V"
    identity: Literal["eeg", "eog", "ecg", "unconfirmed"] = "unconfirmed"
    identity_source: str = "unconfirmed"


class ArtifactReferenceCatalogue(BaseModel):
    recording_id: str
    source_sha256: str = ""
    sampling_rate_hz: float
    sample_count: int
    channels: list[ArtifactReferenceChannel]


class PreprocessingAnalysisChannels(BaseModel):
    recording_id: str
    channels: list[str]


class ChannelIdentitySelection(BaseModel):
    name: str
    identity: Literal["eeg", "eog", "ecg", "unconfirmed"]


class ChannelIdentityUpdate(BaseModel):
    source_sha256: str
    channels: list[ChannelIdentitySelection]
