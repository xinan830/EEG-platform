from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class ChannelMapping:
    fz: str
    pz: str
    oz: str
    f3: Optional[str] = None
    f4: Optional[str] = None


@dataclass(frozen=True)
class RecordingSummary:
    id: str
    original_name: str
    stored_name: str
    extension: str
    created_at: str
    sfreq: Optional[float] = None
    duration_s: Optional[float] = None
    channels: tuple[str, ...] = ()
    mapping: Optional[ChannelMapping] = None
