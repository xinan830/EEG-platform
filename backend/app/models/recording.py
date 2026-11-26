from dataclasses import dataclass
from typing import Optional

from pydantic import BaseModel


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
    import_channel_configuration_snapshot_json: str | None = None
    import_channel_column_mapping_json: str | None = None
    import_acquisition_montage_snapshot_json: str | None = None
    import_montage_configuration_snapshot_json: str | None = None
    import_montage_catalogue_snapshot_json: str | None = None
    analysis_channels: tuple[str, ...] = ()
    channel_identities: tuple[tuple[str, str], ...] = ()


class ImportedProjectRecordingResponse(BaseModel):
    project_id: str
    session_id: str
    subject_id: str | None
    recording_id: str
    original_name: str
    extension: str
    created_at: str
    sfreq: float
    duration_s: float
    channels: list[str]
    analysis_channels: list[str] = []
    channel_types: list[str]
    channel_units: list[str]
    source_sha256: str
    file_size_bytes: int
    import_channel_configuration_snapshot_json: str | None = None
    import_channel_column_mapping_json: str | None = None
    import_acquisition_montage_snapshot_json: str | None = None
    import_montage_configuration_snapshot_json: str | None = None
    import_montage_catalogue_snapshot_json: str | None = None


class RawSampleRangeResponse(BaseModel):
    recording_id: str
    start_sample: int
    end_sample: int
    sampling_rate_hz: float
    channels: list[str]
    channel_units: list[str]
    values_v: list[list[float | None]]
    unavailable_intervals: list[dict[str, object]]
