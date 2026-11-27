import json
import hashlib
from collections.abc import Callable
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.core.config import ALLOWED_EXTENSIONS, DATABASE_PATH, RECORDINGS_DIR, ensure_storage_directories
from app.models.recording import RecordingSummary
from app.persistence import migrate_database
from app.persistence.repositories.recording import RecordingRepository
from app.services.recordings.recording_identity import (
    RECORDING_IMPORT_VERSION,
    backfill_recording_identity,
    canonical_channel_label,
)
from app.services.recordings.recording_samples import WpfRecordingFactsCache, read_edf_bdf_metadata
from app.services.recordings.recording_access import RecordingAccessService
from app.services.recordings.wpf_recording import inspect_wpf_recording


class RecordingService:
    def __init__(self, storage_dir: Path = RECORDINGS_DIR, database_path: Path = DATABASE_PATH):
        self.storage_dir = Path(storage_dir)
        self.database_path = Path(database_path)
        self._catalog = RecordingRepository(self.database_path)
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._wpf_recording_facts = WpfRecordingFactsCache()
        from app.services.recordings.analysis_montage_snapshots import AnalysisMontageSnapshots
        self._analysis_montage_snapshots = AnalysisMontageSnapshots(self.database_path)
        self._recording_access = RecordingAccessService(self.storage_dir, self._wpf_recording_facts,
                                                        self._analysis_montage_snapshots)
        self._delete_listeners: list[Callable[[str], None]] = []
        ensure_storage_directories()
        self._initialize_database()

    def add_delete_listener(self, listener: Callable[[str], None]) -> None:
        self._delete_listeners.append(listener)

    def invalidate_recording(self, recording_id: str) -> None:
        for listener in self._delete_listeners:
            listener(recording_id)

    def _initialize_database(self) -> None:
        migrate_database(self.database_path)
        backfill_recording_identity(self.database_path, self.storage_dir)

    def create_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        normalized_suffix = str(suffix).lower()
        if normalized_suffix not in ALLOWED_EXTENSIONS:
            raise ValueError("仅支持 BDF 或 EDF 文件")
        if not raw_bytes:
            raise ValueError("录制文件不能为空")

        recording_id = uuid4().hex
        stored_name = f"{recording_id}{normalized_suffix}"
        created_at = datetime.now(timezone.utc).isoformat()
        source_sha256 = hashlib.sha256(raw_bytes).hexdigest()
        file_size_bytes = len(raw_bytes)
        (self.storage_dir / stored_name).write_bytes(raw_bytes)

        recording = RecordingSummary(
            id=recording_id,
            original_name=Path(str(original_name)).name,
            stored_name=stored_name,
            extension=normalized_suffix,
            created_at=created_at,
            source_sha256=source_sha256,
            file_size_bytes=file_size_bytes,
            import_version=RECORDING_IMPORT_VERSION,
        )
        self._catalog.insert_source(recording)
        return recording

    def create_imported_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
        recording = self.create_recording(original_name, suffix, raw_bytes)
        try:
            sfreq, duration_s, channels, channel_types, channel_units = read_edf_bdf_metadata(
                self.storage_dir / recording.stored_name
            )
        except Exception:
            self._delete_recording(recording)
            raise ValueError("无法读取 BDF/EDF 脑电文件") from None
        self._set_metadata(
            recording.id, sfreq, duration_s, channels, channel_types, channel_units
        )
        return self.require_recording(recording.id)

    def register_wpf_recording(
        self, source_directory: str, *, signal_mode: str = "raw",
        analysis_montage_profiles: list[dict[str, object]] | None = None,
    ) -> RecordingSummary:
        if analysis_montage_profiles and signal_mode != "acquisition_montage":
            raise ValueError("analysis montage snapshots require acquisition_montage mode")
        prepared_snapshots = None
        if analysis_montage_profiles:
            serialized = [json.dumps(profile, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
                          for profile in analysis_montage_profiles]
            prepared_snapshots = self._analysis_montage_snapshots.prepare(source_directory, serialized)
        registered = self._register_wpf_source(source_directory, signal_mode=signal_mode)
        if prepared_snapshots:
            self._analysis_montage_snapshots.save(registered.id, prepared_snapshots)
            return self.require_recording(registered.id)
        return registered

    def _register_wpf_source(self, source_directory: str, *, signal_mode: str) -> RecordingSummary:
        facts = inspect_wpf_recording(source_directory)
        self._wpf_recording_facts.remember(facts)
        if signal_mode not in {"raw", "acquisition_montage"}:
            raise ValueError("unknown analysis signal mode")
        derived = signal_mode == "acquisition_montage"
        view = None
        if derived:
            from app.services.recordings.acquisition_analysis_signals import read_acquisition_signal_view
            view = read_acquisition_signal_view(facts.root)
        stored_name = f"{'wpf-montage' if derived else 'wpf'}:{facts.root}"
        recording_id = f"{'wpf-montage' if derived else 'wpf'}-{facts.session_id}-{facts.source_sha256[:12]}"
        import_version = "wpf-acquisition-montage-v1" if derived else "wpf-raw-v1"
        raw_channels = [item for item in facts.channels if item.stream_index in facts.eeg_indexes]
        labels = [signal.name for signal in view.signals] if view else [item.label for item in raw_channels]
        source = RecordingSummary(
            id=recording_id, original_name=facts.root.name, stored_name=stored_name,
            extension=".wpf", created_at=facts.recording_start_utc, sfreq=facts.sampling_rate_hz,
            duration_s=facts.duration_s, channels=tuple(labels), source_sha256=facts.source_sha256,
            file_size_bytes=sum(path.stat().st_size for path in facts.root.glob("samples-*.bin")),
            raw_channel_labels=tuple(item.label for item in raw_channels),
            canonical_channel_labels=tuple(labels),
            channel_types=("eeg",) * len(labels) if derived else tuple(item.kind for item in raw_channels),
            channel_units=("V",) * len(labels), import_version=import_version,
        )
        return self.require_recording(self._catalog.register_wpf_source(source))

    def list_recordings(self) -> list[RecordingSummary]:
        return self._catalog.list()

    def require_recording(self, recording_id: str) -> RecordingSummary:
        summary = self._catalog.get(recording_id)
        if summary is None:
            raise KeyError("录制文件不存在")
        from app.services.recordings.recording_channel_roles import attach_recording_identities
        summary = attach_recording_identities(self.storage_dir, summary)
        if summary.stored_name.startswith("wpf-montage:"):
            directory = summary.stored_name.removeprefix("wpf-montage:")
            catalogue = self._analysis_montage_snapshots.catalogue(summary.id, directory)
            return RecordingSummary(**{
                **summary.__dict__,
                "analysis_channels": summary.channels,
                "montage_catalogue": catalogue,
            })
        from app.services.recordings.imported_analysis_signals import enrich_imported_recording_summary
        return enrich_imported_recording_summary(summary)


    def open_access_session(
        self, recording: RecordingSummary, *, analysis_montage_id: str | None = None,
        verify_content_hash: bool = True, bad_channel_spec=None, raw_unprojected: bool = False,
        raw_voltage_channels: bool = False,
        artifact_reference_channels=(),
        scientific_channels: bool = True,
    ):
        return self._recording_access.open_session(
            recording, analysis_montage_id=analysis_montage_id,
            verify_content_hash=verify_content_hash, bad_channel_spec=bad_channel_spec,
            raw_unprojected=raw_unprojected, raw_voltage_channels=raw_voltage_channels,
            artifact_reference_channels=artifact_reference_channels,
            scientific_channels=scientific_channels,
        )

    def select_analysis_montage(self, recording: RecordingSummary, montage_id: str) -> RecordingSummary:
        """Validate a registered montage snapshot and bind its output channels."""
        if recording.stored_name.startswith("wpf-montage:"):
            directory = recording.stored_name.removeprefix("wpf-montage:")
            view = self._analysis_montage_snapshots.resolve(recording.id, directory, montage_id)
        elif recording.import_montage_configuration_snapshot_json:
            from app.services.recordings.imported_analysis_signals import read_imported_acquisition_signal_view
            view = read_imported_acquisition_signal_view(recording, montage_id)
        else:
            raise ValueError("ANALYSIS_MONTAGE_UNSUPPORTED: 该记录没有可验证的导联快照")
        return RecordingSummary(**{
            **recording.__dict__,
            "channels": tuple(signal.name for signal in view.signals),
            "selected_montage_id": montage_id,
        })

    def analysis_signal_provenance(self, recording: RecordingSummary) -> dict:
        from app.services.recordings.recording_channel_roles import role_fingerprint
        if not hasattr(recording, "stored_name"):
            recording = self.require_recording(recording.id)
        provenance = self._analysis_signal_provenance(recording)
        from app.services.recordings.recording_geometry import recording_electrode_geometry
        geometry = recording_electrode_geometry(recording)
        if geometry is not None:
            provenance = {**provenance, "electrode_geometry": geometry}
        fingerprint = role_fingerprint(self.storage_dir, recording) if hasattr(recording, "extension") else None
        return {**provenance, "channel_identity_sha256": fingerprint} if fingerprint else provenance

    def _analysis_signal_provenance(self, recording: RecordingSummary) -> dict:
        if not hasattr(recording, "stored_name"):
            recording = self.require_recording(recording.id)
        if recording.stored_name.startswith("wpf-montage:"):
            return self._analysis_montage_snapshots.resolve(
                recording.id, recording.stored_name.removeprefix("wpf-montage:"), recording.selected_montage_id,
            ).provenance
        if recording.import_montage_configuration_snapshot_json and recording.selected_montage_id:
            from app.services.recordings.imported_analysis_signals import read_imported_acquisition_signal_view
            return read_imported_acquisition_signal_view(
                recording, recording.selected_montage_id,
            ).provenance
        return {"signal_mode": "raw", "analysis_reference": "original_recording_no_software_rereference"}

    def _set_metadata(
        self,
        recording_id: str,
        sfreq: float,
        duration_s: float,
        channels: list[str],
        channel_types: list[str],
        channel_units: list[str],
    ) -> None:
        raw_labels = [str(name) for name in channels]
        canonical = [canonical_channel_label(name) for name in raw_labels]
        if len({name.casefold() for name in canonical}) != len(canonical):
            raise ValueError("规范化通道标签存在重复")
        self._catalog.update_channels(
            recording_id, sfreq, duration_s, raw_labels, canonical, channel_types, channel_units,
            RECORDING_IMPORT_VERSION,
        )

    def _delete_recording(self, recording: RecordingSummary) -> None:
        from app.services.recordings.recording_channel_roles import identity_path
        self.invalidate_recording(recording.id)
        (self.storage_dir / recording.stored_name).unlink(missing_ok=True)
        self._catalog.delete(recording.id)
        identity_path(self.storage_dir, recording).unlink(missing_ok=True)

    def delete_import_if_unreferenced(self, recording_id: str) -> bool:
        """Delete an imported EDF/BDF blob only after its project and analysis references are gone."""
        recording = self.require_recording(recording_id)
        if recording.extension.casefold() not in {".edf", ".bdf"}:
            return False
        if self._catalog.is_referenced(recording_id):
            return False
        source = (self.storage_dir / recording.stored_name).resolve()
        if source.parent != self.storage_dir.resolve() or not source.is_file():
            return False
        try:
            self._delete_recording(recording)
            return True
        except OSError:
            return False
