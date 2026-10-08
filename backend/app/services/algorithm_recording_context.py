"""Run-scoped recording access; arrays remain V/float64 in sample order."""

from __future__ import annotations

from typing import Any
import numpy as np
from app.scientific.primitives import SpectralEstimate
from app.algorithm_runtime.execution_control import ExecutionControl
from app.services.mapped_preprocessing import mapped_preprocess

class _RecordingAlgorithmContext:
    """Bounded signal access passed to a canonical algorithm module."""

    def __init__(self, recordings: Any, recording: Any, execution_context: ExecutionControl | None = None) -> None:
        self._recordings = recordings
        self._recording = recording
        self.id = recording.id
        self.channel_names = list(recording.channels)
        self.sfreq_hz = float(getattr(recording, "sfreq_hz", None) or getattr(recording, "sfreq", None) or 0.0)
        self.duration_s = float(recording.duration_s or 0.0)
        self.execution_context = execution_context
        self._signals: dict[tuple, Any] = {}
        self._reader: Any = None

    def close(self) -> None:
        self._signals.clear()
        if self._reader is not None:
            self._reader.close()
            self._reader = None

    def load_spectrum(self, *, start_s: float, window_s: float, channels: list[str], low_hz: float = 1.0, high_hz: float = 30.0, notch_hz: float | None = None, filter_low_hz: float | None = None, filter_high_hz: float | None = None, output_low_hz: float | None = None, output_high_hz: float | None = None) -> SpectralEstimate:
        payload = self._recordings.load_spectrum(self._recording, start_s, window_s, channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz, filter_low_hz=filter_low_hz, filter_high_hz=filter_high_hz, output_low_hz=output_low_hz, output_high_hz=output_high_hz, execution_context=self.execution_context)
        ordered = list(payload["channels"])
        selected = ordered[0]
        evidence = {
            "sfreq_hz": float(payload["sfreq_hz"]),
            "analysis_reference": payload["analysis_reference"],
            "algorithm_version": payload["algorithm_version"],
            "welch_contract": payload["welch_contract"],
            "filter_contract": dict(payload["filter_contract"]),
            "filter_frequency_range_hz": dict(payload["filter_frequency_range_hz"]),
            "output_frequency_range_hz": dict(payload["frequency_range_hz"]),
            "units": payload["units"],
            "frequencies_hz": list(payload["frequencies_hz"]),
            "channels": ordered,
            "psd_uV2_per_hz": list(payload["psd"][selected]),
            "band_power": dict(payload["band_power"][selected]),
            "relative_band_power": dict(payload["relative_band_power"][selected]),
            "quality": dict(payload["quality"]),
        }
        return SpectralEstimate(
            np.asarray(payload["frequencies_hz"], dtype=float),
            np.asarray([payload["psd"][name] for name in ordered], dtype=float) * 1e-12,
            float(payload["quality"]["clean_ratio"]),
            int(payload["quality"]["clean_segments"]),
            int(payload["quality"]["total_segments"]),
            payload["quality"]["gate_failed"],
            tuple(payload["quality"].get("rejected_reasons", [])),
            evidence,
        )

    def load_spectrogram(self, *, start_s: float, window_s: float, channels: list[str], low_hz: float = 1.0, high_hz: float = 50.0, notch_hz: float | None = None, output_low_hz: float | None = None, output_high_hz: float | None = None) -> dict[str, object]:
        """Load the frozen spectrogram contract through the recording service boundary."""
        return self._recordings.load_spectrogram(
            self._recording,
            start_s=start_s,
            window_s=window_s,
            channels=channels,
            low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            output_low_hz=output_low_hz if output_low_hz is not None else low_hz,
            output_high_hz=output_high_hz if output_high_hz is not None else high_hz,
            execution_context=self.execution_context,
        )

    def load_faa_signals(self, *, start_s: float, end_s: float, f3_channel: str, f4_channel: str, low_hz: float | None = None, high_hz: float | None = None, notch_hz: float | None = None) -> tuple[np.ndarray, np.ndarray, float, dict[str, Any]]:
        """Slice the pair after whole-recording continuous preprocessing."""
        names = tuple(self.channel_names)
        lookup = {name.casefold(): (index, name) for index, name in enumerate(names)}
        try:
            f3_index, f3_name = lookup[f3_channel.casefold()]
            f4_index, f4_name = lookup[f4_channel.casefold()]
        except KeyError as exc:
            raise ValueError(f"FAA source channel does not exist: {exc.args[0]}") from exc
        sfreq = self.sfreq_hz
        start_index = max(0, int(np.floor(start_s * sfreq)))
        end_index = min(int(round(self.duration_s * sfreq)), int(round(end_s * sfreq)))
        if end_index <= start_index:
            raise ValueError("FAA analysis range is outside recording duration")
        if low_hz is not None and high_hz is not None:
            key = (f3_name, f4_name, low_hz, high_hz, notch_hz)
            if key not in self._signals:
                self._signals[key] = mapped_preprocess(self._recordings, self._recording,
                    channels=[f3_name, f4_name], low_hz=low_hz, high_hz=high_hz,
                    notch_hz=notch_hz, execution_context=self.execution_context)
            pair = self._signals[key].data[start_index:end_index]
        else:
            if self._reader is None:
                self._reader = self._recordings.open_sample_reader(self._recording)
            pair = self._reader.read(start_index, end_index, [f3_index, f4_index])
        evidence = {
            "sfreq_hz": float(sfreq), "channels": [f3_name, f4_name],
            "analysis_reference": "original_recording_no_software_rereference",
            "time_scope": "exact_requested_absolute_range",
            "preprocessing_scope": "whole_recording_before_window_slice" if low_hz is not None and high_hz is not None else "none",
            "preprocessing_range_s": {"start_s": 0.0, "end_s": self.duration_s},
            "requested_range_s": {"start_s": float(start_s), "end_s": float(end_s)},
            "actual_range_s": {"start_s": float(start_index / sfreq), "end_s": float(end_index / sfreq)},
            "faa_contract": {
                "method": "paired_epoch_rfft_density",
                "epoch_s": 2.0,
                "overlap_fraction": 0.5,
                "step_s": 1.0,
                "window": "hann",
                "detrend": "per_epoch_mean_removal",
                "software_bandpass": None if low_hz is None or high_hz is None else [float(low_hz), float(high_hz)],
                "notch_hz": None if notch_hz is None else float(notch_hz),
                "minimum_clean_epochs": 10,
                "artifact_peak_uv": 150.0,
                "alpha_band_hz": [8.0, 13.0],
                "frequency_resolution_hz": 1.0 / 2.0,
            },
        }
        return pair[:, 0], pair[:, 1], float(sfreq), evidence
