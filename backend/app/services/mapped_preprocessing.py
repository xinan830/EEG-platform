"""Owned temporary maps for unchanged continuous offline preprocessing."""

from __future__ import annotations

import tempfile
import shutil
import weakref
from pathlib import Path
from typing import Any

import numpy as np

from app.scientific.primitives import preprocess_blocks
from app.algorithm_runtime.execution_control import ExecutionControl
from app.services.analysis_preprocess_cache import PreprocessedRecording
from app.algorithm_runtime.errors import DynamicWindowResourceExceededError


class MappedArrays:
    def __init__(self, resource_policy=None):
        self.directory = tempfile.TemporaryDirectory(prefix="brain-analysis-")
        self.maps: dict[int, np.memmap] = {}
        self.sequence = 0
        self.resource_policy = resource_policy
        # Registered after TemporaryDirectory's finalizer so mmap handles close
        # before Windows attempts directory cleanup during interpreter exit.
        self._finalizer = weakref.finalize(self, _cleanup_maps, self.maps, self.directory)

    def allocate(self, shape) -> np.memmap:
        requested = int(np.prod(shape)) * np.dtype("float64").itemsize
        if self.resource_policy is not None:
            used = sum(array.nbytes for array in self.maps.values())
            free = shutil.disk_usage(self.directory.name).free
            if (used + requested > self.resource_policy.max_preparation_bytes or
                    free - requested < self.resource_policy.minimum_free_disk_bytes):
                raise DynamicWindowResourceExceededError("连续预处理临时文件超出资源预算。",
                    detail={"used_bytes": used, "requested_bytes": requested, "free_bytes": free,
                            "limit_bytes": self.resource_policy.max_preparation_bytes,
                            "minimum_free_disk_bytes": self.resource_policy.minimum_free_disk_bytes})
        self.sequence += 1
        array = np.memmap(Path(self.directory.name) / f"array-{self.sequence}.bin",
                          mode="w+", dtype="float64", shape=shape)
        self.maps[id(array)] = array
        return array

    def release(self, array: np.ndarray) -> None:
        mapped = self.maps.pop(id(array), None)
        if mapped is not None:
            path = Path(mapped.filename)
            mapped._mmap.close()
            path.unlink(missing_ok=True)

    def close(self) -> None:
        self._finalizer()


def _cleanup_maps(maps, directory):
    for mapped in list(maps.values()):
        mapped._mmap.close()
    maps.clear()
    directory.cleanup()


def mapped_preprocess(recordings: Any, recording: Any, *, low_hz: float,
                      high_hz: float, notch_hz: float | None, notch_q: float = 30.0,
                      channels: list[str] | None = None, execution_context: ExecutionControl | None = None,
                      bad_channel_spec: Any = None, artifact_rejection_spec: Any = None,
                      artifact_removal_spec: Any = None) -> PreprocessedRecording:
    policy = getattr(execution_context, "preparation_resource_policy", None)
    owner = MappedArrays() if policy is None else MappedArrays(policy)
    session = None
    def check_cancelled():
        if execution_context is not None:
            execution_context.check_cancelled()
    try:
        session = (
            recordings.open_access_session(recording)
            if bad_channel_spec is None
            else recordings.open_access_session(recording, bad_channel_spec=bad_channel_spec)
        )
        lookup = {name.casefold(): name for name in session.channel_names}
        names = tuple(session.channel_names) if channels is None else tuple(lookup[name.casefold()] for name in channels)
        shape = (session.sample_count, len(names))
        source = owner.allocate(shape)
        target = owner.allocate(shape)
        block_samples = max(1, min(65536, (8 * 1024**2) // (8 * len(names))))
        for start in range(0, session.sample_count, block_samples):
            check_cancelled()
            stop = min(start + block_samples, session.sample_count)
            source[start:stop] = session.read_samples(
                start, stop, names, check_cancelled=check_cancelled,
            ).values_v
        preprocess_blocks(source, target, session.sampling_rate_hz, owner.allocate,
                          low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
                          notch_q=notch_q, check_cancelled=check_cancelled, release=owner.release,
                          block_samples=block_samples)
        artifact_intervals: tuple[Any, ...] = ()
        if artifact_rejection_spec is not None and artifact_rejection_spec.normalized().is_active:
            from app.scientific.artifact_segments import apply_artifact_rejection_mask, detect_artifact_segments

            check_cancelled()
            norm_art = artifact_rejection_spec.normalized()
            # Use wideband detrended signal for PTP if the analysis low-pass is narrow (<45 Hz),
            # and broadband source for muscle z-score so 110-140 Hz EMG is never attenuated.
            ptp_source = target
            wide_hi = min(45.0, (session.sampling_rate_hz / 2.0) - 1.5)
            if norm_art.peak_to_peak_uv is not None and high_hz < 45.0 and wide_hi > max(high_hz, 1.0):
                from app.services.artifact_inspection import detrend_and_filter_for_inspection

                ptp_source, _ = detrend_and_filter_for_inspection(
                    source, session.sampling_rate_hz,
                    low_hz=min(low_hz, 0.5), high_hz=wide_hi, notch_hz=notch_hz,
                )
            report = detect_artifact_segments(
                ptp_source, session.sampling_rate_hz, names, norm_art, muscle_values_v=source,
            )
            artifact_intervals = report.segments
            if norm_art.policy == "reject" and artifact_intervals:
                apply_artifact_rejection_mask(target, artifact_intervals, names, in_place=True)
        owner.release(source)
        artifact_removal_evidence: dict[str, object] | None = None
        if artifact_removal_spec is not None and artifact_removal_spec.normalized().is_active:
            from app.scientific.artifact_removal import apply_offline_artifact_removal, fit_ica_model
            from app.services.ica_review_cache import compute_ica_fit_fingerprint

            check_cancelled()
            norm_rem = artifact_removal_spec.normalized()
            prefitted_ica = None
            ica_fit_fp: str | None = None
            if norm_rem.method == "ica_infomax":
                identity = getattr(session, "identity", None)
                ica_fit_fp = compute_ica_fit_fingerprint(
                    recording_id=getattr(identity, "recording_id", getattr(recording, "id", "")),
                    source_sha256=getattr(identity, "source_sha256", getattr(recording, "source_sha256", "") or ""),
                    signal_view_sha256=getattr(identity, "signal_view_sha256", None),
                    channel_names=names,
                    sampling_rate_hz=session.sampling_rate_hz,
                    sample_count=session.sample_count,
                    low_hz=low_hz,
                    high_hz=high_hz,
                    notch_hz=notch_hz,
                    bad_channel_spec=bad_channel_spec,
                    artifact_rejection_spec=artifact_rejection_spec,
                    removal_spec=norm_rem,
                )
                if norm_rem.ica_fit_fingerprint and norm_rem.ica_fit_fingerprint != ica_fit_fp:
                    raise ValueError(
                        "ICA_FIT_FINGERPRINT_MISMATCH: 请求应用的 ICA 审查指纹与当前前置预处理状态（导联/滤波/坏导/坏段/成分数）不一致，请重新审查 ICA 成分"
                    )
                ica_cache = getattr(recordings, "ica_review_cache", None)
                prefitted_ica = ica_cache.get(ica_fit_fp) if ica_cache is not None else None
                if prefitted_ica is None and ica_cache is not None:
                    fitted = fit_ica_model(target, session.sampling_rate_hz, names, norm_rem)
                    ica_cache.put(ica_fit_fp, fitted, recording_id=getattr(recording, "id", ""))
                    removal_result = apply_offline_artifact_removal(
                        target, session.sampling_rate_hz, names, norm_rem,
                        prefitted_ica=fitted, ica_fit_fingerprint=ica_fit_fp,
                    )
                    ev = removal_result.to_evidence()
                    ev["ica_model_reused"] = False
                    target[:] = removal_result.data_v
                    artifact_removal_evidence = ev
                else:
                    removal_result = apply_offline_artifact_removal(
                        target, session.sampling_rate_hz, names, norm_rem,
                        prefitted_ica=prefitted_ica, ica_fit_fingerprint=ica_fit_fp,
                    )
                    target[:] = removal_result.data_v
                    artifact_removal_evidence = removal_result.to_evidence()
            else:
                removal_result = apply_offline_artifact_removal(
                    target, session.sampling_rate_hz, names, norm_rem,
                )
                target[:] = removal_result.data_v
                artifact_removal_evidence = removal_result.to_evidence()
        target.flush()
        return PreprocessedRecording(
            target, session.sampling_rate_hz, names, owner,
            artifact_intervals=artifact_intervals,
            artifact_removal_evidence=artifact_removal_evidence,
        )
    except BaseException:
        owner.close()
        raise
    finally:
        if session is not None:
            session.close()
