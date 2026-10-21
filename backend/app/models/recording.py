from dataclasses import dataclass
from typing import Optional


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
    source_sha256: Optional[str] = None
    file_size_bytes: Optional[int] = None
    raw_channel_labels: tuple[str, ...] = ()
    canonical_channel_labels: tuple[str, ...] = ()
    channel_types: tuple[str, ...] = ()
    channel_units: tuple[str, ...] = ()
    import_version: str = "legacy-unversioned"
    # Immutable historical analysis montage catalogue captured with WPF records.
    # Empty means the record is readable but intentionally non-switchable.
    montage_catalogue: tuple[dict[str, object], ...] = ()
    selected_montage_id: str | None = None
