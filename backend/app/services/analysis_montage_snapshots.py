"""Validate and persist explicit profile inputs separately from acquisition files."""

from __future__ import annotations

import hashlib
import json
import threading
from collections import OrderedDict
from dataclasses import replace
from datetime import datetime, timezone
from pathlib import Path

from app.persistence import connect_database
from app.services.acquisition_analysis_signals import (
    AcquisitionSignalView, _catalogue_entry, _field,
    historical_montage_catalogue, read_acquisition_signal_view,
)
from app.services.wpf_recording import WpfRecordingError


class AnalysisMontageSnapshots:
    """A snapshot's content identity never follows later edits to a user preset."""

    def __init__(self, database_path: Path):
        self.database_path = database_path
        self._resolved_views: OrderedDict[tuple[str, str, str, str], AcquisitionSignalView] = OrderedDict()
        self._cache_lock = threading.Lock()
        self._resolved_view_limit = 32

    def prepare(self, directory: str | Path, profiles_json: list[str]) -> list[tuple[str, str, str, str]]:
        captured = historical_montage_catalogue(directory)
        pending = []
        # Validate all candidates before opening the write transaction.
        for raw_json in profiles_json:
            try:
                profile = json.loads(raw_json)
                if not isinstance(profile, dict):
                    raise ValueError("profile must be an object")
            except (TypeError, ValueError) as exc:
                raise WpfRecordingError("ANALYSIS_MONTAGE_INVALID", "分析导联定义不是有效对象") from exc
            revision = _field(profile, "Revision")
            if (not isinstance(_field(profile, "Id"), str) or not _field(profile, "Id").strip()
                    or not isinstance(revision, int) or isinstance(revision, bool) or revision < 1):
                raise WpfRecordingError("ANALYSIS_MONTAGE_INVALID", "分析导联缺少有效配置身份或版本")
            try:
                view = read_acquisition_signal_view(directory, profile_snapshot=profile)
            except WpfRecordingError as exc:
                raise WpfRecordingError("ANALYSIS_MONTAGE_INCOMPATIBLE", str(exc)) from exc
            if any(self._same_projection(view, entry) for entry in captured):
                continue
            canonical = json.dumps(profile, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
            digest = view.provenance["snapshot_sha256"]
            snapshot_id = f"analysis-montage-{digest}"
            pending.append((snapshot_id, canonical, digest, datetime.now(timezone.utc).isoformat()))
        return pending

    def save(self, recording_id: str, pending: list[tuple[str, str, str, str]]) -> None:
        rows = [(recording_id, *item) for item in pending]
        with connect_database(self.database_path, foreign_keys=True) as connection:
            connection.execute("BEGIN IMMEDIATE")
            connection.executemany(
                "INSERT OR IGNORE INTO analysis_montage_snapshots VALUES (?, ?, ?, ?, ?)", rows,
            )
            connection.commit()

    @staticmethod
    def _same_projection(view: AcquisitionSignalView, entry: dict) -> bool:
        value = view.provenance
        return (str(value.get("montage_id")) == entry["id"]
                and str(value.get("montage_revision")) == entry["revision"]
                and value["source_mapping"] == entry["source_mapping"]
                and value["formulas"] == entry["formulas"])

    def resolve(self, recording_id: str, directory: str | Path, montage_id: str | None) -> AcquisitionSignalView:
        if not montage_id:
            return read_acquisition_signal_view(directory, montage_id)
        with connect_database(self.database_path) as connection:
            row = connection.execute(
                "SELECT * FROM analysis_montage_snapshots WHERE recording_id = ? AND snapshot_id = ?",
                (recording_id, montage_id),
            ).fetchone()
        if row is None:
            return read_acquisition_signal_view(directory, montage_id)
        manifest_path = Path(directory) / "manifest.json"
        manifest_digest = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        cache_key = (recording_id, str(Path(directory).resolve()), row["profile_json"], manifest_digest)
        with self._cache_lock:
            view = self._resolved_views.get(cache_key)
            if view is not None:
                self._resolved_views.move_to_end(cache_key)
        profile = json.loads(row["profile_json"])
        if view is None:
            view = read_acquisition_signal_view(directory, profile_snapshot=profile)
        digest = view.provenance["snapshot_sha256"]
        if digest != row["snapshot_sha256"] or montage_id != f"analysis-montage-{digest}":
            raise WpfRecordingError("ANALYSIS_MONTAGE_INTEGRITY_FAILED", "分析导联快照或记录绑定已改变")
        with self._cache_lock:
            if cache_key not in self._resolved_views:
                self._resolved_views[cache_key] = view
                self._resolved_views.move_to_end(cache_key)
                while len(self._resolved_views) > self._resolved_view_limit:
                    self._resolved_views.popitem(last=False)
        return replace(view, provenance={
            **view.provenance,
            "snapshot_source": "analysis_configuration_snapshot",
            "analysis_reference": "analysis_configuration_snapshot",
            "source_profile_id": _field(profile, "Id"),
            "montage_id": montage_id,
            "montage_revision": str(_field(profile, "Revision")),
            "montage_fingerprint": digest,
        })

    def catalogue(self, recording_id: str, directory: str | Path) -> tuple[dict, ...]:
        with connect_database(self.database_path) as connection:
            rows = connection.execute(
                "SELECT snapshot_id, snapshot_sha256 FROM analysis_montage_snapshots WHERE recording_id = ? ORDER BY created_at, snapshot_id",
                (recording_id,),
            ).fetchall()
        entries = []
        for row in rows:
            view = self.resolve(recording_id, directory, row["snapshot_id"])
            entry = _catalogue_entry(view)
            entry.update({
                "id": row["snapshot_id"],
                "revision": str(view.provenance["montage_revision"]),
                "fingerprint": row["snapshot_sha256"],
                "snapshot_source": "analysis_configuration_snapshot",
                "source_profile_id": view.provenance["source_profile_id"],
            })
            entries.append(entry)
        return tuple(entries)
