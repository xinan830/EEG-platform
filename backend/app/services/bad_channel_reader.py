"""Pre-rereference bad-channel sample readers and montage exclusion helpers.

Applies bad-channel spherical spline interpolation or CAR-safe exclusion strictly
BEFORE montage re-referencing, keeping raw files untouched and NaN gaps explicit.
"""

from __future__ import annotations

import hashlib
import json
from typing import Sequence

import numpy as np

from app.scientific.bad_channels import (
    SphericalSplineInterpolationPlan,
    build_spherical_spline_interpolator,
)
from app.scientific.contracts.preprocessing import BadChannelSpec
from app.services.acquisition_analysis_signals import AcquisitionSignal, AcquisitionSignalView


class BadChannelInterpolatingSampleReader:
    """Stream-compatible SampleReader that replaces declared bad channels via MNE spherical splines."""

    def __init__(self, raw, plan: SphericalSplineInterpolationPlan) -> None:
        self.raw = raw
        self.plan = plan
        self.sfreq = raw.sfreq
        self.sample_count = raw.sample_count
        self.names = tuple(raw.names)
        self._bad_position = {idx: pos for pos, idx in enumerate(plan.bad_indices)}
        self._good_indices = list(plan.good_indices)

    def read(
        self,
        start: int,
        stop: int,
        channels: list[int] | None = None,
        check_cancelled=None,
    ) -> np.ndarray:
        selected = list(range(len(self.names))) if channels is None else list(channels)
        requested_bads = [idx for idx in selected if idx in self._bad_position]
        if not requested_bads:
            return self.raw.read(start, stop, selected, check_cancelled)

        # Read every requested good channel plus all interpolation donor good channels once.
        needed_raw = list(dict.fromkeys([
            *(idx for idx in selected if idx not in self._bad_position),
            *self._good_indices,
        ]))
        raw_block = np.asarray(self.raw.read(start, stop, needed_raw, check_cancelled), dtype=np.float64)
        col_lookup = {raw_idx: pos for pos, raw_idx in enumerate(needed_raw)}
        good_block = raw_block[:, [col_lookup[idx] for idx in self._good_indices]]
        interpolated_all = good_block @ self.plan.weights.T
        invalid_samples = ~np.all(np.isfinite(good_block), axis=1)
        if np.any(invalid_samples):
            interpolated_all[invalid_samples, :] = np.nan

        output = np.empty((stop - start, len(selected)), dtype=np.float64)
        for out_pos, ch_idx in enumerate(selected):
            bad_pos = self._bad_position.get(ch_idx)
            if bad_pos is not None:
                output[:, out_pos] = interpolated_all[:, bad_pos]
            else:
                output[:, out_pos] = raw_block[:, col_lookup[ch_idx]]
        return output

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]:
        return self.raw.source_gaps(start, stop)

    def close(self) -> None:
        self.raw.close()


class BadChannelExcludingSampleReader:
    """Raw-mode SampleReader that removes excluded bad channels cleanly before downstream analysis."""

    def __init__(self, raw, kept_indices: Sequence[int]) -> None:
        self.raw = raw
        self.sfreq = raw.sfreq
        self.sample_count = raw.sample_count
        self._kept_indices = tuple(int(i) for i in kept_indices)
        self.names = tuple(raw.names[i] for i in self._kept_indices)

    def read(
        self,
        start: int,
        stop: int,
        channels: list[int] | None = None,
        check_cancelled=None,
    ) -> np.ndarray:
        selected = list(range(len(self._kept_indices))) if channels is None else list(channels)
        mapped = [self._kept_indices[i] for i in selected]
        return self.raw.read(start, stop, mapped, check_cancelled)

    def source_gaps(self, start: int, stop: int) -> tuple[range, ...]:
        return self.raw.source_gaps(start, stop)

    def close(self) -> None:
        self.raw.close()


def apply_bad_channels_to_raw_reader(
    reader,
    bad_spec: BadChannelSpec | None,
    *,
    channel_names: Sequence[str],
    channel_kinds: Sequence[str | None] | None = None,
    electrode_geometry: dict[str, object] | None = None,
) -> tuple[object, tuple[str, ...], str | None]:
    """Apply pre-rereference bad-channel handling to a raw (non-montage) reader."""
    if bad_spec is None:
        return reader, tuple(channel_names), None
    norm = bad_spec.normalized()
    if not norm.is_active:
        return reader, tuple(channel_names), None

    names = tuple(channel_names)
    reader.names = names
    if norm.policy == "interpolate":
        wrapped, plan = wrap_reader_with_bad_channel_interpolation(
            reader, norm, channel_kinds=channel_kinds, electrode_geometry=electrode_geometry,
        )
        view_sha = _combined_view_sha256("raw", {**norm.to_evidence(), "interpolation": plan.to_evidence()})
        return wrapped, names, view_sha

    lookup = {name.casefold(): idx for idx, name in enumerate(names)}
    bad_folded: set[str] = set()
    for ch in norm.channels:
        folded = ch.casefold()
        if folded not in lookup:
            raise ValueError(f"BAD_CHANNEL_NOT_FOUND: 坏导通道 {ch} 不存在于原始通道中")
        bad_folded.add(folded)
    kept_indices = [idx for idx, name in enumerate(names) if name.casefold() not in bad_folded]
    if not kept_indices:
        raise ValueError("BAD_CHANNEL_ALL_OUTPUTS_EXCLUDED: 排除坏导后没有可用的通道")
    wrapped = BadChannelExcludingSampleReader(reader, kept_indices)
    view_sha = _combined_view_sha256("raw", norm.to_evidence())
    return wrapped, wrapped.names, view_sha


def apply_bad_channels_to_montage_reader(
    raw_reader,
    view: AcquisitionSignalView,
    bad_spec: BadChannelSpec | None,
    *,
    channel_kinds: Sequence[str | None] | None = None,
    electrode_geometry: dict[str, object] | None = None,
) -> tuple[object, AcquisitionSignalView]:
    """Apply bad-channel interpolation or CAR-safe exclusion BEFORE AcquisitionMontageSampleReader."""
    if bad_spec is None:
        return raw_reader, view
    norm = bad_spec.normalized()
    if not norm.is_active:
        return raw_reader, view
    wrapped_raw, plan = wrap_reader_with_bad_channel_interpolation(
        raw_reader, norm, channel_kinds=channel_kinds, electrode_geometry=electrode_geometry,
    )
    updated_view = apply_bad_channel_policy_to_montage_view(
        view, norm, interpolation_plan=plan,
    )
    return wrapped_raw, updated_view


def inspect_recording_bad_channels(
    recordings,
    recording,
    *,
    start_s: float = 0.0,
    duration_s: float = 60.0,
    max_cells: int = 4_000_000,
) -> dict[str, object]:
    """Run bounded deterministic EEG bad-channel screening on a raw unprojected recording."""
    from app.scientific.bad_channels import detect_bad_channels

    with recordings.open_access_session(recording, raw_unprojected=True) as session:
        sfreq = float(session.sampling_rate_hz)
        total = int(session.sample_count)
        start_sample = max(0, min(total, int(round(float(start_s) * sfreq))))
        requested_samples = max(1, int(round(float(duration_s) * sfreq)))
        max_samples = max(1, max_cells // max(1, len(session.channel_names)))
        end_sample = min(total, start_sample + min(requested_samples, max_samples))
        if end_sample <= start_sample:
            raise ValueError("坏导检测时间窗超出录音有效范围")
        block = session.read_samples(start_sample, end_sample)
        channel_types = (
            recording.channel_types
            if len(recording.channel_types) == len(block.channel_names)
            else None
        )
        report = detect_bad_channels(
            block.values_v,
            sfreq,
            block.channel_names,
            channel_types=channel_types,
            electrode_geometry=recordings.analysis_signal_provenance(recording).get("electrode_geometry"),
        )
    payload = report.to_dict()
    payload.update({
        "recording_id": recording.id,
        "start_sample": start_sample,
        "end_sample": end_sample,
        "start_s": round(start_sample / sfreq, 6),
        "end_s": round(end_sample / sfreq, 6),
    })
    return payload


def wrap_reader_with_bad_channel_interpolation(
    reader,
    bad_spec: BadChannelSpec,
    *,
    channel_kinds: Sequence[str | None] | None = None,
    electrode_geometry: dict[str, object] | None = None,
) -> tuple[object, SphericalSplineInterpolationPlan | None]:
    """Wrap raw SampleReader when policy is 'interpolate'; no-op otherwise."""
    norm = bad_spec.normalized()
    if norm.policy != "interpolate":
        return reader, None
    plan = build_spherical_spline_interpolator(
        reader.names,
        norm.channels,
        channel_kinds=channel_kinds,
        electrode_geometry=electrode_geometry,
    )
    return BadChannelInterpolatingSampleReader(reader, plan), plan


def apply_bad_channel_policy_to_montage_view(
    view: AcquisitionSignalView,
    bad_spec: BadChannelSpec,
    *,
    interpolation_plan: SphericalSplineInterpolationPlan | None = None,
) -> AcquisitionSignalView:
    """Update a montage view and its provenance hash for bad-channel exclusion or interpolation."""
    norm = bad_spec.normalized()
    if not norm.is_active:
        return view

    provenance = dict(view.provenance)
    bad_folded = {label.casefold() for label in norm.channels}

    if norm.policy == "interpolate":
        bad_info = {
            **norm.to_evidence(),
            "interpolation": interpolation_plan.to_evidence() if interpolation_plan is not None else None,
        }
        provenance["bad_channel_handling"] = bad_info
        provenance["snapshot_sha256"] = _combined_view_sha256(provenance.get("snapshot_sha256"), bad_info)
        return AcquisitionSignalView(view.signals, provenance)

    # Policy == "exclude": never allow excluded bad channels to contaminate CAR pools
    # or remain as valid montage outputs.
    kept_signals: list[AcquisitionSignal] = []
    excluded_outputs: list[dict[str, str]] = []
    for signal in view.signals:
        if signal.positive.casefold() in bad_folded:
            excluded_outputs.append({"name": signal.name, "reason": "bad_channel_excluded"})
            continue
        if signal.kind == "mean":
            filtered_negatives = tuple(label for label in signal.negatives if label.casefold() not in bad_folded)
            if len(filtered_negatives) < 2:
                raise ValueError(
                    f"BAD_CHANNEL_REFERENCE_UNAVAILABLE: 排除坏导后平均参考 {signal.name} 可用参考通道不足 2 路"
                )
            kept_signals.append(AcquisitionSignal(signal.name, signal.positive, filtered_negatives, signal.kind))
        elif any(label.casefold() in bad_folded for label in signal.negatives):
            excluded_outputs.append({"name": signal.name, "reason": "bad_reference_channel_excluded"})
        else:
            kept_signals.append(signal)

    if not kept_signals:
        raise ValueError("BAD_CHANNEL_ALL_OUTPUTS_EXCLUDED: 排除坏导后没有可用的导联输出通道")

    bad_info = {
        **norm.to_evidence(),
        "excluded_outputs": excluded_outputs,
        "car_bad_channels_excluded": True,
    }
    provenance["bad_channel_handling"] = bad_info
    provenance["formulas"] = [
        {"name": s.name, "positive": s.positive, "negative_kind": s.kind, "negative_labels": list(s.negatives)}
        for s in kept_signals
    ]
    provenance["snapshot_sha256"] = _combined_view_sha256(provenance.get("snapshot_sha256"), bad_info)
    return AcquisitionSignalView(tuple(kept_signals), provenance)


def predict_excluded_output_channels(
    channel_names: Sequence[str],
    bad_spec: BadChannelSpec,
    signal_provenance: dict[str, object] | None = None,
) -> tuple[str, ...]:
    """Predict output channels remaining after applying bad-channel exclusion before re-referencing."""
    norm = bad_spec.normalized()
    if not norm.is_active or norm.policy != "exclude":
        return tuple(channel_names)
    bad_folded = {label.casefold() for label in norm.channels}
    formulas = (signal_provenance or {}).get("formulas")
    if isinstance(formulas, list) and formulas:
        kept: list[str] = []
        for entry in formulas:
            if not isinstance(entry, dict):
                continue
            name = str(entry.get("name", ""))
            positive = str(entry.get("positive", ""))
            kind = str(entry.get("negative_kind", "")).casefold()
            negatives = [str(label) for label in (entry.get("negative_labels") or ())]
            if positive.casefold() in bad_folded:
                continue
            if kind == "mean":
                if len([lbl for lbl in negatives if lbl.casefold() not in bad_folded]) >= 2:
                    kept.append(name)
            elif not any(lbl.casefold() in bad_folded for lbl in negatives):
                kept.append(name)
        return tuple(kept)
    return tuple(ch for ch in channel_names if ch.casefold() not in bad_folded)


def _combined_view_sha256(base_sha256: object, bad_info: dict[str, object]) -> str:
    payload = json.dumps(
        {"base_snapshot_sha256": str(base_sha256 or ""), "bad_channel_handling": bad_info},
        sort_keys=True,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()
