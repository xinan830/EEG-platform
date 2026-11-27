"""Paired scientific comparison preview payloads (before vs after offline preprocessing)."""

from __future__ import annotations

import math
from typing import Any, Sequence

import numpy as np

from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
    DEFAULT_NOTCH_Q,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.services.preprocessing.preview_envelope import (
    MAX_PAIRED_PREVIEW_POINTS_PER_TRACE, MAX_PAIRED_PREVIEW_RESPONSE_SCALARS,
    build_paired_preview_envelope,
)


def _verify_paired_preparations(baseline: Any, processed: Any, *, bad_channel_active: bool) -> None:
    if float(baseline.sfreq) != float(processed.sfreq):
        raise ValueError("PREVIEW_SOURCE_IDENTITY_MISMATCH: 基线与处理侧采样率不一致")
    if int(baseline.data.shape[0]) != int(processed.data.shape[0]):
        raise ValueError("PREVIEW_SOURCE_IDENTITY_MISMATCH: 基线与处理侧总样本数不一致")
    base_id = getattr(baseline, "source_identity", None)
    proc_id = getattr(processed, "source_identity", None)
    if base_id is not None and proc_id is not None:
        attrs = ("recording_id", "source_sha256") if bad_channel_active else ("recording_id", "source_sha256", "signal_view_sha256")
        for attr in attrs:
            if getattr(base_id, attr, None) != getattr(proc_id, attr, None):
                raise ValueError(f"PREVIEW_SOURCE_IDENTITY_MISMATCH: 基线与处理侧来源身份 {attr} 不一致")


def _match_processed_channels(
    baseline_channels: Sequence[str],
    processed_channels: Sequence[str],
    *,
    allow_exclusions: bool,
) -> tuple[dict[str, int], list[str]]:
    base_lookup = {str(name).casefold(): str(name) for name in baseline_channels}
    proc_index: dict[str, int] = {}
    for col_i, ch in enumerate(processed_channels):
        key = str(ch).casefold()
        if key not in base_lookup:
            raise ValueError(f"PREVIEW_CHANNEL_MISMATCH: 处理侧包含基线中不存在的输出通道 {ch!r}")
        if key in proc_index:
            raise ValueError(f"PREVIEW_CHANNEL_MISMATCH: 处理侧包含重复的输出通道 {ch!r}")
        proc_index[key] = col_i

    excluded_outputs = [str(ch) for ch in baseline_channels if str(ch).casefold() not in proc_index]
    if excluded_outputs and not allow_exclusions:
        raise ValueError("PREVIEW_CHANNEL_MISMATCH: 未启用坏导排除时处理侧缺少基线通道")
    return proc_index, excluded_outputs


def build_preprocessing_preview_window(
    recordings: Any,
    recording: Any,
    *,
    start_s: float,
    window_s: float,
    montage_id: str | None = None,
    low_hz: float = 1.0,
    high_hz: float = 50.0,
    notch_hz: float | None = 50.0,
    notch_q: float = DEFAULT_NOTCH_Q,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_rejection_spec: ArtifactRejectionSpec | None = None,
    artifact_removal_spec: ArtifactRemovalSpec | None = None,
    max_points: int = 1200,
    offline_preprocessing=None,
) -> dict[str, object]:
    """Return paired display-only scientific envelopes (shared filter baseline vs processed)."""
    if offline_preprocessing is None:
        from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
        offline_preprocessing = OfflineScientificPreprocessingService.for_recordings(recordings)
    target_recording = recording
    if montage_id is not None:
        target_recording = recordings.select_analysis_montage(recording, montage_id)

    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    norm_rem = artifact_removal_spec.normalized() if artifact_removal_spec is not None else ArtifactRemovalSpec()
    enhanced_active = norm_bad.is_active or norm_art.is_active or norm_rem.is_active

    filter_spec = ScientificFilterSpec(
        low_hz=float(low_hz),
        high_hz=float(high_hz),
        notch_hz=None if notch_hz is None else float(notch_hz),
        notch_q=float(notch_q),
        filter_phase="zero_phase",
        scope="whole_recording_before_window_slice",
    )
    baseline_spec = OfflinePreprocessingSpec(filter_spec=filter_spec)
    processed_spec = OfflinePreprocessingSpec(
        filter_spec=filter_spec,
        bad_channels=norm_bad,
        artifact_rejection=norm_art,
        artifact_removal=norm_rem,
    )

    baseline = offline_preprocessing.prepare_continuous(
        target_recording,
        low_hz=low_hz,
        high_hz=high_hz,
        notch_hz=notch_hz,
        preprocessing_spec=baseline_spec,
    )
    if enhanced_active:
        processed = offline_preprocessing.prepare_continuous(
            target_recording,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
            bad_channel_spec=norm_bad if norm_bad.is_active else None,
            artifact_rejection_spec=norm_art if norm_art.is_active else None,
            artifact_removal_spec=norm_rem if norm_rem.is_active else None,
            preprocessing_spec=processed_spec,
        )
        _verify_paired_preparations(baseline, processed,
            bad_channel_active=norm_bad.is_active or bool(norm_rem.eog_channels or norm_rem.ecg_channels))
    else:
        processed = baseline

    sfreq = float(baseline.sfreq)
    total = int(baseline.data.shape[0])
    s_idx = max(0, min(total, int(math.floor(float(start_s) * sfreq))))
    e_idx = max(s_idx, min(total, s_idx + max(1, int(round(float(window_s) * sfreq)))))
    if e_idx <= s_idx:
        raise ValueError("预览窗口超出录音范围")

    base_channels = [str(ch) for ch in baseline.channel_names]
    proc_channels = [str(ch) for ch in processed.channel_names]
    proc_col_lookup, excluded_output_channels = _match_processed_channels(
        base_channels, proc_channels,
        allow_exclusions=(norm_bad.policy == "exclude" or bool(norm_rem.eog_channels or norm_rem.ecg_channels)),
    )
    if hasattr(recording, "extension"):
        from app.services.recordings.recording_channel_roles import scientific_channel_names
        scientific_names = {name.casefold() for name in scientific_channel_names(recordings, target_recording)}
        excluded_output_channels.extend(name for name in target_recording.channels
            if name.casefold() not in scientific_names and name not in excluded_output_channels)

    base_slice = np.asarray(baseline.data[s_idx:e_idx], dtype=np.float64)
    proc_slice = base_slice if processed is baseline else np.asarray(processed.data[s_idx:e_idx], dtype=np.float64)

    base_times_map: dict[str, list[float]] = {}
    base_uv_map: dict[str, list[float | None]] = {}
    proc_times_map: dict[str, list[float]] = {}
    proc_uv_map: dict[str, list[float | None]] = {}

    remaining_scalars = MAX_PAIRED_PREVIEW_RESPONSE_SCALARS
    for base_col, base_name in enumerate(base_channels):
        proc_col = proc_col_lookup.get(base_name.casefold())
        b_arr = base_slice[:, base_col]
        p_arr = b_arr if processed is baseline or proc_col is None else proc_slice[:, proc_col]
        scalars_per_point = 2 if proc_col is None else 4
        times, base_uv, proc_uv = build_paired_preview_envelope(
            b_arr, p_arr, start_sample=s_idx, sfreq=sfreq, target_points=max_points,
            max_output_points=min(MAX_PAIRED_PREVIEW_POINTS_PER_TRACE, remaining_scalars // scalars_per_point),
        )
        remaining_scalars -= len(times) * scalars_per_point
        base_times_map[base_name] = times
        base_uv_map[base_name] = base_uv
        if proc_col is not None:
            proc_times_map[proc_channels[proc_col]] = list(times)
            proc_uv_map[proc_channels[proc_col]] = proc_uv

    provenance = recordings.analysis_signal_provenance(target_recording) if hasattr(recordings, "analysis_signal_provenance") else {}
    formulas = {str(row["name"]).casefold(): row for row in (provenance or {}).get("formulas", [])}
    bad_set = {ch.casefold() for ch in norm_bad.channels}

    def depends_on_interpolated_input(channel: str) -> bool:
        formula = formulas.get(channel.casefold())
        inputs = [formula["positive"], *formula.get("negative_labels", [])] if formula else [channel]
        return any(str(label).casefold() in bad_set for label in inputs)

    interpolated = [
        ch for ch in proc_channels
        if norm_bad.policy == "interpolate" and depends_on_interpolated_input(ch)
    ]
    excluded = list(norm_bad.channels) if norm_bad.policy == "exclude" else []

    from app.scientific.artifact_removal_validity import project_application_validity

    rem_ev = processed.artifact_removal_evidence or {}
    base_id = getattr(baseline, "source_identity", None)
    source_sha256 = getattr(base_id, "source_sha256", None) or getattr(recording, "source_sha256", None)
    montage_sha256 = getattr(base_id, "signal_view_sha256", None) or (provenance or {}).get("snapshot_sha256")

    return {
        "schema_version": "scientific-preview-comparison-v1",
        "recording_id": recording.id,
        "source_sha256": source_sha256,
        "montage_id": montage_id,
        "montage_snapshot_sha256": montage_sha256,
        "start_sample": s_idx,
        "end_sample": e_idx,
        "start_s": round(s_idx / sfreq, 6),
        "end_s": round(e_idx / sfreq, 6),
        "sfreq_hz": sfreq,
        "unit": "µV",
        "common_filter": filter_spec.to_evidence(),
        "enhanced_preprocessing_active": enhanced_active,
        "display_compression": {
            "method": "paired_finite_run_min_max_envelope_v1",
            "scientific_resampling": False,
            "target_points_per_channel": max_points,
            "gap_boundaries_may_exceed_target": True,
        },
        "baseline": {
            "role": "scientific_baseline",
            "channels": base_channels,
            "trace_times_s": base_times_map,
            "traces_uv": base_uv_map,
            "preprocessing_evidence": baseline_spec.to_evidence(),
        },
        "processed": {
            "role": "scientific_processed",
            "channels": proc_channels,
            "excluded_output_channels": excluded_output_channels,
            "interpolated_channels": interpolated,
            "excluded_channels": excluded,
            "trace_times_s": proc_times_map,
            "traces_uv": proc_uv_map,
            "preprocessing_evidence": processed_spec.to_evidence(),
            "ica_model_reused": bool(rem_ev.get("ica_model_reused", False)),
            "artifact_removal_status": str(rem_ev.get("status", "skipped")),
            "artifact_removal_validity": project_application_validity(
                rem_ev.get("application_validity"), start_sample=s_idx, end_sample=e_idx,
            ),
        },
    }
