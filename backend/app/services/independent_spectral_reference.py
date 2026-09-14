"""Independent SciPy/MNE reference evidence for the frozen spectral contract.

This module intentionally does not import production spectral calculation
helpers.  It may read recording metadata through the application, but filtering,
quality selection and Welch PSD estimation are independently implemented here.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy import signal

from app.core.provenance import sha256_json
from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT
from app.models.recording import RecordingSummary
from app.models.run import ValidationCreateRequest, ValidationRun
from app.models.spectral_validation import SpectralReferenceValidationRequest
from app.services.recordings import RecordingService
from app.services.validations import ValidationService


REFERENCE_IMPLEMENTATION = "independent-scipy-mne-spectral-reference-v1"
PSD_RTOL = 1e-7
PSD_ATOL_UV2_HZ = 1e-9


class SpectralReferenceUnavailable(ValueError):
    """The comparison cannot honestly produce a finite PSD vector."""

    def __init__(self, reason: str, message: str):
        super().__init__(message)
        self.reason = reason


@dataclass(frozen=True)
class _ReferencePSD:
    freqs: np.ndarray
    psd_uv2_hz: np.ndarray
    quality: dict[str, object]
    actual_range: dict[str, float]
    channels: list[str]


SourceReader = Callable[[RecordingSummary], tuple[np.ndarray, float, list[str]]]


class IndependentSpectralReferenceService:
    """Persist pointwise agreement between production and independent PSD paths."""

    def __init__(
        self,
        recordings: RecordingService,
        validations: ValidationService,
        source_reader: SourceReader | None = None,
    ):
        self.recordings = recordings
        self.validations = validations
        self._source_reader = source_reader or self._read_with_mne

    def validate(
        self,
        recording: RecordingSummary,
        request: SpectralReferenceValidationRequest,
    ) -> ValidationRun:
        requested_window_s = request.end_s - request.start_s
        try:
            production = self.recordings.load_spectrum(
                recording,
                start_s=request.start_s,
                window_s=requested_window_s,
                channels=request.channels,
            )
        except ValueError as exc:
            raise SpectralReferenceUnavailable("production_psd_unavailable", str(exc)) from exc

        try:
            reference = self._calculate_reference(recording, request, production["channels"])
        except ValueError as exc:
            raise SpectralReferenceUnavailable("reference_psd_unavailable", str(exc)) from exc

        production_freqs = np.asarray(production["frequencies_hz"], dtype=float)
        if not np.array_equal(reference.freqs, production_freqs):
            raise SpectralReferenceUnavailable(
                "frequency_axis_mismatch",
                "独立参考实现与生产 PSD 的频率坐标不一致",
            )
        production_psd = np.asarray(
            [production["psd"][channel] for channel in reference.channels], dtype=float
        )
        if production_psd.shape != reference.psd_uv2_hz.shape or not np.isfinite(production_psd).all():
            raise SpectralReferenceUnavailable(
                "production_psd_unavailable", "生产 PSD 不是有限的预期形状",
            )

        actual_range = reference.actual_range
        config = {
            "reference_implementation": REFERENCE_IMPLEMENTATION,
            "analysis_algorithm_version": ANALYSIS_CONTRACT["algorithm_version"],
            "actual_range": actual_range,
            "channels": reference.channels,
            "sfreq_hz": float(production["sfreq_hz"]),
            "reference": ANALYSIS_CONTRACT["reference"],
            "filter": {
                "type": ANALYSIS_CONTRACT["bandpass_type"],
                "order": ANALYSIS_CONTRACT["bandpass_prototype_order"],
                "bandpass_hz": ANALYSIS_CONTRACT["bandpass_hz"],
                "phase": ANALYSIS_CONTRACT["preprocessing_phase"],
                "form": ANALYSIS_CONTRACT["filter_form"],
            },
            "welch": {
                "segment_s": ANALYSIS_CONTRACT["welch_segment_s"],
                "overlap": ANALYSIS_CONTRACT["welch_segment_overlap"],
                "window": ANALYSIS_CONTRACT["welch_window"],
                "scaling": ANALYSIS_CONTRACT["welch_scaling"],
                "detrend": "constant",
            },
            "unit": "uV^2/Hz",
            "ordering": "requested_channel_then_ascending_frequency",
        }
        evidence: dict[str, object] = {
            "schema_version": "independent-spectral-reference-evidence-v1",
            "scope": "engineering_validation_only_not_clinical_validation",
            "reference_implementation": REFERENCE_IMPLEMENTATION,
            "requested_range": {"start_s": request.start_s, "end_s": request.end_s},
            "actual_range": actual_range,
            "channels": reference.channels,
            "frequencies_hz": reference.freqs.tolist(),
            "unit": "uV^2/Hz",
            "ordering": "requested_channel_then_ascending_frequency",
            "reference_psd": {
                channel: reference.psd_uv2_hz[index].tolist()
                for index, channel in enumerate(reference.channels)
            },
            "production_psd": {
                channel: production_psd[index].tolist()
                for index, channel in enumerate(reference.channels)
            },
            "reference_quality": reference.quality,
            "production_quality": production["quality"],
        }
        identity = {
            "recording_id": recording.id,
            "source_sha256": recording.source_sha256,
            "file_size_bytes": recording.file_size_bytes,
        }
        return self.validations.create(
            ValidationCreateRequest(
                kind="independent_scipy_spectral_psd",
                algorithm_id="offline-spectral-v3",
                algorithm_version=str(ANALYSIS_CONTRACT["algorithm_version"]),
                dataset_identity=identity,
                config_sha256=sha256_json(config),
                tolerances={"rtol": PSD_RTOL, "atol": PSD_ATOL_UV2_HZ},
                expected=reference.psd_uv2_hz.reshape(-1).tolist(),
                actual=production_psd.reshape(-1).tolist(),
            ),
            evidence=evidence,
        )

    def _calculate_reference(
        self,
        recording: RecordingSummary,
        request: SpectralReferenceValidationRequest,
        production_channels: list[str],
    ) -> _ReferencePSD:
        source, sfreq, source_channels = self._source_reader(recording)
        values = np.asarray(source, dtype=np.float64)
        if values.ndim != 2 or len(values) == 0:
            raise ValueError("独立参考读取到的 EEG 不是非空二维数组")
        if not np.isfinite(sfreq) or sfreq <= 0:
            raise ValueError("独立参考读取到的采样率无效")
        lookup = {name.casefold(): index for index, name in enumerate(source_channels)}
        try:
            indexes = [lookup[name.casefold()] for name in production_channels]
        except KeyError as exc:
            raise ValueError("独立参考读取缺少生产分析通道") from exc

        low, high = (float(value) for value in ANALYSIS_CONTRACT["bandpass_hz"])
        if high >= sfreq / 2.0:
            raise ValueError("独立参考分析高切必须低于奈奎斯特频率")
        sos = signal.butter(
            int(ANALYSIS_CONTRACT["bandpass_prototype_order"]),
            [low, high], btype="bandpass", fs=sfreq, output="sos",
        )
        try:
            filtered_all = signal.sosfiltfilt(sos, values, axis=0)
        except ValueError as exc:
            raise ValueError("记录太短，无法完成独立零相位滤波") from exc

        start_index = int(np.floor(request.start_s * sfreq))
        start_index = max(0, min(start_index, len(filtered_all)))
        requested_samples = int(round((request.end_s - request.start_s) * sfreq))
        stop_index = min(len(filtered_all), start_index + requested_samples)
        window = filtered_all[start_index:stop_index, indexes]
        segment_samples = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
        if len(window) < segment_samples:
            raise ValueError("独立参考频谱窗口至少需要 4 秒")
        step_samples = max(1, int(round(segment_samples * (1.0 - float(ANALYSIS_CONTRACT["welch_segment_overlap"])))) )
        segments = [window[offset:offset + segment_samples] for offset in range(0, len(window) - segment_samples + 1, step_samples)]
        checks = [self._quality(segment, segment_samples) for segment in segments]
        clean = [segment for segment, check in zip(segments, checks) if check["status"] == "clean"]
        clean_ratio = len(clean) / len(segments) if segments else 0.0
        if not clean or clean_ratio < float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"]):
            reasons = list(dict.fromkeys(reason for check in checks for reason in check["reasons"]))
            raise ValueError("独立参考质量门未通过：" + ", ".join(reasons or ["low_quality"]))

        individual_psd: list[np.ndarray] = []
        frequencies = np.array([], dtype=float)
        for segment in clean:
            frequencies, density = signal.welch(
                segment,
                fs=sfreq,
                window=str(ANALYSIS_CONTRACT["welch_window"]),
                nperseg=segment_samples,
                noverlap=0,
                detrend="constant",
                scaling=str(ANALYSIS_CONTRACT["welch_scaling"]),
                axis=0,
            )
            individual_psd.append(density.T)
        frequency_mask = (frequencies >= 1.0) & (frequencies <= 30.0)
        averaged = np.maximum(np.mean(np.stack(individual_psd), axis=0)[:, frequency_mask], 1e-20)
        return _ReferencePSD(
            freqs=frequencies[frequency_mask],
            psd_uv2_hz=averaged * 1e12,
            quality={
                "clean_segments": len(clean),
                "total_segments": len(segments),
                "clean_ratio": clean_ratio,
                "rejected_reasons": list(dict.fromkeys(reason for check in checks for reason in check["reasons"])),
            },
            actual_range={
                "start_s": start_index / sfreq,
                "end_s": stop_index / sfreq,
                "duration_s": (stop_index - start_index) / sfreq,
            },
            channels=list(production_channels),
        )

    @staticmethod
    def _quality(values: np.ndarray, expected_samples: int) -> dict[str, object]:
        """Independent copy of the public quality contract; no production helper."""
        reasons: list[str] = []
        if values.ndim != 2 or len(values) != expected_samples:
            reasons.append("missing_samples")
        finite = values.ndim == 2 and bool(np.isfinite(values).all())
        if not finite:
            reasons.append("non_finite")
            peak_uv: float | None = None
        else:
            peak_uv = float(np.max(np.abs(values)) * 1e6) if values.size else 0.0
            if peak_uv > float(ANALYSIS_CONTRACT["artifact_peak_uv"]):
                reasons.append("amplitude_threshold")
            if values.size and np.any(np.ptp(values, axis=0) * 1e6 < float(ANALYSIS_CONTRACT["flatline_peak_to_peak_uv"])):
                reasons.append("flatline")
            if values.size and IndependentSpectralReferenceService._has_clipping(values):
                reasons.append("clipping")
        unique = list(dict.fromkeys(reasons))
        return {"status": "bad" if unique else "clean", "reasons": unique, "peak_uv": peak_uv}

    @staticmethod
    def _has_clipping(values: np.ndarray) -> bool:
        minimum_count = int(ANALYSIS_CONTRACT["clipping_minimum_samples"])
        required = max(minimum_count, int(np.ceil(len(values) * float(ANALYSIS_CONTRACT["clipping_minimum_ratio"]))))
        if required > len(values):
            return False
        for channel in values.T:
            low, high = np.min(channel), np.max(channel)
            if np.count_nonzero(channel == low) >= required:
                return True
            if high != low and np.count_nonzero(channel == high) >= required:
                return True
        return False

    def _read_with_mne(self, recording: RecordingSummary) -> tuple[np.ndarray, float, list[str]]:
        """Read BDF/EDF independently of `RecordingService.load_data`."""
        import mne

        path = Path(self.recordings.storage_dir) / recording.stored_name
        reader = mne.io.read_raw_bdf if recording.extension == ".bdf" else mne.io.read_raw_edf
        raw = reader(path, preload=True, verbose=False)
        try:
            return np.asarray(raw.get_data(), dtype=np.float64).T, float(raw.info["sfreq"]), list(raw.ch_names)
        finally:
            raw.close()
