"""Bounded cache binding reviewed ICA models to verified preprocessing fingerprints."""

from __future__ import annotations

from collections import OrderedDict
import hashlib
import json
from threading import RLock
from typing import Any, Sequence

from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec


def compute_ica_fit_fingerprint(
    *,
    recording_id: str,
    source_sha256: str,
    signal_view_sha256: str | None,
    channel_names: Sequence[str],
    sampling_rate_hz: float,
    sample_count: int,
    low_hz: float,
    high_hz: float,
    notch_hz: float | None,
    notch_q: float = 30.0,
    bad_channel_spec: BadChannelSpec | None,
    artifact_rejection_spec: ArtifactRejectionSpec | None,
    removal_spec: ArtifactRemovalSpec,
) -> str:
    """Deterministic fingerprint of every input that affects fitted ICA unmixing weights.

    Excludes `ica_exclude_components` and `ica_fit_fingerprint` so choosing different
    components on the same reviewed ICA model reuses the exact same model without drift.
    """
    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    norm_rem = removal_spec.normalized()
    payload = json.dumps(
        {
            "version": "ica-fit-v1",
            "recording_id": str(recording_id),
            "source_sha256": str(source_sha256),
            "signal_view_sha256": str(signal_view_sha256 or ""),
            "channels": [str(ch) for ch in channel_names],
            "sampling_rate_hz": round(float(sampling_rate_hz), 6),
            "sample_count": int(sample_count),
            "low_hz": round(float(low_hz), 6),
            "high_hz": round(float(high_hz), 6),
            "notch_hz": None if notch_hz is None else round(float(notch_hz), 6),
            "notch_q": round(float(notch_q), 6),
            "bad_channels": norm_bad.fingerprint(),
            "artifact_rejection": norm_art.fingerprint(),
            "eog_channels": sorted(ch.casefold() for ch in norm_rem.eog_channels),
            "ecg_channels": sorted(ch.casefold() for ch in norm_rem.ecg_channels),
            "allow_frontal_eog_proxy": bool(norm_rem.allow_frontal_eog_proxy),
            "ica_n_components": norm_rem.ica_n_components,
            "ica_eog_threshold": round(float(norm_rem.ica_eog_threshold), 6),
            "random_state": int(norm_rem.random_state),
        },
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:24]


class IcaReviewCache:
    """Thread-safe bounded LRU cache for reviewed FittedIcaModel objects."""

    def __init__(self, max_entries: int = 6) -> None:
        self._max_entries = max(1, int(max_entries))
        self._lock = RLock()
        self._entries: OrderedDict[str, tuple[str, Any]] = OrderedDict()

    def get(self, fingerprint: str) -> Any | None:
        with self._lock:
            entry = self._entries.get(fingerprint)
            if entry is None:
                return None
            self._entries.move_to_end(fingerprint)
            return entry[1]

    def put(self, fingerprint: str, model: Any, *, recording_id: str) -> None:
        with self._lock:
            self._entries[fingerprint] = (str(recording_id), model)
            self._entries.move_to_end(fingerprint)
            while len(self._entries) > self._max_entries:
                self._entries.popitem(last=False)

    def invalidate_recording(self, recording_id: str) -> int:
        with self._lock:
            keys = [fp for fp, (rec_id, _model) in self._entries.items() if rec_id == recording_id]
            for fp in keys:
                self._entries.pop(fp, None)
            return len(keys)

    def clear(self) -> None:
        with self._lock:
            self._entries.clear()
