"""Validate and persist explicit profile inputs separately from acquisition files."""

from __future__ import annotations

import hashlib
import json
import threading
from collections import OrderedDict
from copy import deepcopy
from dataclasses import replace
from datetime import datetime, timezone
from pathlib import Path

from app.persistence import connect_database
from app.core.performance_trace import current_performance_trace, measure_performance
from app.services.acquisition_analysis_signals import (
    AcquisitionSignalView, _catalogue_entry, _catalogue_projection_key, _field,
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
        self._catalogues: OrderedDict[tuple, tuple[dict, ...]] = OrderedDict()
        self._catalogue_limit = 8

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

    @measure_performance("montage_snapshot_resolve")
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
        manifest_digest = hashlib.sha256((Path(directory) / "manifest.json").read_bytes()).hexdigest()
        return self._resolve_row(recording_id, directory, row, manifest_digest)

    def _resolve_row(self, recording_id: str, directory: str | Path,
                     row, manifest_digest: str) -> AcquisitionSignalView:
        montage_id = row["snapshot_id"]
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
        return replace(view, provenance=deepcopy({
            **view.provenance,
            "snapshot_source": "analysis_configuration_snapshot",
            "analysis_reference": "analysis_configuration_snapshot",
            "source_profile_id": _field(profile, "Id"),
            "montage_id": montage_id,
            "montage_revision": str(_field(profile, "Revision")),
            "montage_fingerprint": digest,
        }))

    @measure_performance("montage_catalogue")
    def catalogue(self, recording_id: str, directory: str | Path) -> tuple[dict, ...]:
        """Read captured and saved choices once per complete, verified identity."""
        with connect_database(self.database_path) as connection:
            rows = connection.execute(
                "SELECT * FROM analysis_montage_snapshots WHERE recording_id = ? ORDER BY created_at, snapshot_id",
                (recording_id,),
            ).fetchall()
        trace = current_performance_trace()
        if trace is not None:
            trace.increment("montage_snapshot_rows", len(rows))
        try:
            manifest_digest = hashlib.sha256((Path(directory) / "manifest.json").read_bytes()).hexdigest()
        except OSError:
            if rows:
                raise
            return ()
        cache_key = (recording_id, str(Path(directory).resolve()), manifest_digest,
                     tuple(tuple(row) for row in rows))
        with self._cache_lock:
            cached = self._catalogues.get(cache_key)
            if cached is not None:
                self._catalogues.move_to_end(cache_key)
                if trace is not None:
                    trace.increment("montage_catalogue_cache_hits")
                return deepcopy(cached)
        if trace is not None:
            trace.increment("montage_catalogue_builds")
        try:
            entries = list(historical_montage_catalogue(directory))
        except Exception:
            # A damaged source still needs to reach analysis admission so it can
            # publish a structured unavailable result instead of failing catalog lookup.
            entries = []
        seen = {_catalogue_projection_key(entry) for entry in entries}
        seen_ids = {str(entry.get("id")) for entry in entries}
        for row in rows:
            if row["snapshot_id"] in seen_ids:
                continue
            view = self._resolve_row(recording_id, directory, row, manifest_digest)
            entry = _catalogue_entry(view)
            entry.update({
                "id": row["snapshot_id"],
                "revision": str(view.provenance["montage_revision"]),
                "fingerprint": row["snapshot_sha256"],
                "snapshot_source": "analysis_configuration_snapshot",
                "source_profile_id": view.provenance["source_profile_id"],
            })
            projection_key = _catalogue_projection_key(entry)
            if projection_key in seen:
                continue
            seen.add(projection_key)
            seen_ids.add(row["snapshot_id"])
            entries.append(entry)
        if hashlib.sha256((Path(directory) / "manifest.json").read_bytes()).hexdigest() != manifest_digest:
            raise WpfRecordingError("ANALYSIS_MONTAGE_INTEGRITY_FAILED", "读取导联目录期间记录清单发生变化")
        result = tuple(entries)
        with self._cache_lock:
            self._catalogues[cache_key] = deepcopy(result)
            self._catalogues.move_to_end(cache_key)
            while len(self._catalogues) > self._catalogue_limit:
                self._catalogues.popitem(last=False)
        return result
