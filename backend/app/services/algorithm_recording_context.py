"""Run-scoped recording access; arrays remain V/float64 in sample order."""

from __future__ import annotations

import logging
import time
from typing import Any
import numpy as np
from app.scientific.primitives import SpectralEstimate
from app.algorithm_runtime.execution_control import ExecutionControl
from app.algorithm_runtime.signal_source import SignalSourceData, SignalSourceRequest, SpectralSourceRequest
from app.services.mapped_preprocessing import mapped_preprocess

_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")

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
        self._access_session: Any = None

    def close(self) -> None:
        self._signals.clear()
        if self._access_session is not None:
            self._access_session.close()
            self._access_session = None

    def signal_source_identity(self) -> tuple[str, str]:
        """Bind a source request to its immutable raw or montage identity."""
        source = self._recordings.analysis_signal_provenance(self._recording)
        montage_id = source.get("montage_id")
        fingerprint = source.get("montage_fingerprint") or source.get("snapshot_sha256")
        if montage_id and fingerprint:
            return str(montage_id), str(fingerprint)
        return (
            f"raw-recording:{self._recording.id}",
            str(getattr(self._recording, "source_sha256", None) or self._recording.id),
        )

    def load_spectral_source(
        self,
        request: SpectralSourceRequest,
        *,
        role_to_channel: dict[str, str],
    ) -> SpectralEstimate:
        """Resolve a typed spectral request through the existing Welch service."""
        if request.representation != "spectrum":
            raise ValueError("Welch loader requires a spectrum request")
        self._validate_spectral_request(request, role_to_channel)
        start_s = request.start_sample / self.sfreq_hz
        window_s = (request.end_sample - request.start_sample) / self.sfreq_hz
        channels = [role_to_channel[role] for role in request.roles]
        payload = self._recordings.load_spectrum(
            self._recording, start_s, window_s, channels,
            filter_low_hz=request.filter_low_hz,
            filter_high_hz=request.filter_high_hz,
            output_low_hz=request.output_low_hz,
            output_high_hz=request.output_high_hz,
            notch_hz=request.notch_hz,
            preprocessing_scope=request.preprocessing_scope,
            execution_context=self.execution_context,
        )
        estimate = self._spectral_estimate(payload)
        estimate.evidence["typed_source_request"] = self._spectral_request_evidence(request, channels)
        return estimate

    def load_spectrogram_source(
        self,
        request: SpectralSourceRequest,
        *,
        role_to_channel: dict[str, str],
    ) -> dict[str, object]:
        """Resolve a typed spectrogram request through the existing STFT service."""
        if request.representation != "spectrogram":
            raise ValueError("spectrogram loader requires a spectrogram request")
        self._validate_spectral_request(request, role_to_channel)
        start_s = request.start_sample / self.sfreq_hz
        window_s = (request.end_sample - request.start_sample) / self.sfreq_hz
        channels = [role_to_channel[role] for role in request.roles]
        payload = self._recordings.load_spectrogram(
            self._recording, start_s, window_s, channels,
            low_hz=request.filter_low_hz,
            high_hz=request.filter_high_hz,
            output_low_hz=request.output_low_hz,
            output_high_hz=request.output_high_hz,
            notch_hz=request.notch_hz,
            execution_context=self.execution_context,
        )
        payload["typed_source_request"] = self._spectral_request_evidence(request, channels)
        return payload

    def _validate_spectral_request(
        self,
        request: SpectralSourceRequest,
        role_to_channel: dict[str, str],
    ) -> None:
        if set(role_to_channel) != set(request.roles):
            raise ValueError("spectral source roles must resolve to exactly the requested channels")
        if not np.isclose(request.expected_sfreq_hz, self.sfreq_hz, rtol=0.0, atol=1e-12):
            raise ValueError("spectral source sampling rate changed after request planning")
        identity = self.signal_source_identity()
        if (request.montage_id, request.montage_fingerprint) != identity:
            raise ValueError("spectral source montage identity changed after request planning")
        if request.start_sample < 0 or request.end_sample > int(round(self.duration_s * self.sfreq_hz)):
            raise ValueError("spectral source sample bounds exceed the recording")

    @staticmethod
    def _spectral_request_evidence(
        request: SpectralSourceRequest,
        channels: list[str],
    ) -> dict[str, Any]:
        return {
            "roles": list(request.roles), "channel_order": channels,
            "start_sample": request.start_sample, "end_sample": request.end_sample,
            "sfreq_hz": request.expected_sfreq_hz,
            "signal_input_unit": "V",
            "service_density_unit": "uV^2/Hz",
            "algorithm_density_unit": "V^2/Hz",
            "montage_id": request.montage_id,
            "montage_fingerprint": request.montage_fingerprint,
            "preprocessing_scope": request.preprocessing_scope,
            "filter_low_hz": request.filter_low_hz,
            "filter_high_hz": request.filter_high_hz,
            "output_low_hz": request.output_low_hz,
            "output_high_hz": request.output_high_hz,
            "notch_hz": request.notch_hz,
            "representation": request.representation,
        }

    def load_spectrum(self, *, start_s: float, window_s: float, channels: list[str], low_hz: float = 1.0, high_hz: float = 30.0, notch_hz: float | None = None, filter_low_hz: float | None = None, filter_high_hz: float | None = None, output_low_hz: float | None = None, output_high_hz: float | None = None, preprocessing_scope: str = "configured_continuous_before_slice") -> SpectralEstimate:
        sfreq = self.sfreq_hz
        start_sample = max(0, int(np.floor(start_s * sfreq)))
        end_sample = min(
            int(round(self.duration_s * sfreq)),
            start_sample + int(round(window_s * sfreq)),
        )
        filter_low = low_hz if filter_low_hz is None else filter_low_hz
        filter_high = high_hz if filter_high_hz is None else filter_high_hz
        output_low = filter_low if output_low_hz is None else output_low_hz
        output_high = filter_high if output_high_hz is None else output_high_hz
        montage_id, montage_fingerprint = self.signal_source_identity()
        request = SpectralSourceRequest(
            roles=tuple(channels), start_sample=start_sample, end_sample=end_sample,
            expected_sfreq_hz=sfreq, montage_id=montage_id,
            montage_fingerprint=montage_fingerprint,
            filter_low_hz=float(filter_low), filter_high_hz=float(filter_high),
            output_low_hz=float(output_low), output_high_hz=float(output_high),
            notch_hz=notch_hz, preprocessing_scope=preprocessing_scope,
        )
        return self.load_spectral_source(request, role_to_channel=dict(zip(channels, channels)))

    @staticmethod
    def _spectral_estimate(payload: dict[str, Any]) -> SpectralEstimate:
        ordered = list(payload["channels"])
        selected = ordered[0]
        evidence = {
            "sfreq_hz": float(payload["sfreq_hz"]),
            "analysis_reference": payload["analysis_reference"],
            "signal_source": payload.get("signal_source"),
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
        """Build a typed sample-bounded request for the frozen STFT service."""
        sfreq = self.sfreq_hz
        start_sample = max(0, int(np.floor(start_s * sfreq)))
        end_sample = min(
            int(round(self.duration_s * sfreq)),
            start_sample + int(round(window_s * sfreq)),
        )
        montage_id, montage_fingerprint = self.signal_source_identity()
        request = SpectralSourceRequest(
            roles=tuple(channels), start_sample=start_sample, end_sample=end_sample,
            expected_sfreq_hz=sfreq, montage_id=montage_id,
            montage_fingerprint=montage_fingerprint,
            filter_low_hz=float(low_hz), filter_high_hz=float(high_hz),
            output_low_hz=float(output_low_hz if output_low_hz is not None else low_hz),
            output_high_hz=float(output_high_hz if output_high_hz is not None else high_hz),
            notch_hz=notch_hz, representation="spectrogram",
        )
        return self.load_spectrogram_source(request, role_to_channel=dict(zip(channels, channels)))

    def load_faa_signals(self, *, start_s: float, end_s: float, f3_channel: str, f4_channel: str, low_hz: float | None = None, high_hz: float | None = None, notch_hz: float | None = None) -> tuple[np.ndarray, np.ndarray, float, dict[str, Any]]:
        """Slice the pair after whole-recording continuous preprocessing."""
        names = tuple(self.channel_names)
        lookup = {name.casefold(): name for name in names}
        try:
            f3_name = lookup[f3_channel.casefold()]
            f4_name = lookup[f4_channel.casefold()]
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
            if self._access_session is None:
                self._access_session = self._recordings.open_access_session(self._recording)
            check_cancelled = self.execution_context.check_cancelled if self.execution_context is not None else None
            pair = self._access_session.read_samples(
                start_index, end_index, (f3_name, f4_name), check_cancelled=check_cancelled,
            ).values_v
        evidence = {
            "sfreq_hz": float(sfreq), "channels": [f3_name, f4_name],
            "analysis_reference": self._recordings.analysis_signal_provenance(self._recording)["analysis_reference"],
            "signal_source": self._recordings.analysis_signal_provenance(self._recording),
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

    def load_signal_source(
        self,
        request: SignalSourceRequest,
        *,
        role_to_channel: dict[str, str],
    ) -> SignalSourceData:
        """Resolve FAA's paired sample request into the typed source contract."""
        if request.preprocessing_scope != "configured_continuous_before_slice":
            raise ValueError("FAA source requires configured continuous preprocessing")
        if set(role_to_channel) != set(request.roles) or len(request.roles) != 2:
            raise ValueError("FAA source roles must resolve to exactly two declared channels")
        if not np.isclose(request.expected_sfreq_hz, self.sfreq_hz, rtol=0.0, atol=1e-12):
            raise ValueError("signal source sampling rate changed after request planning")
        montage_id, montage_fingerprint = self.signal_source_identity()
        if (request.montage_id, request.montage_fingerprint) != (montage_id, montage_fingerprint):
            raise ValueError("signal source montage identity changed after request planning")
        start_s = request.start_sample / self.sfreq_hz
        end_s = request.end_sample / self.sfreq_hz
        ordered_channels = [role_to_channel[role] for role in request.roles]
        left, right, sfreq, evidence = self.load_faa_signals(
            start_s=start_s,
            end_s=end_s,
            f3_channel=ordered_channels[0],
            f4_channel=ordered_channels[1],
            low_hz=request.low_hz,
            high_hz=request.high_hz,
            notch_hz=request.notch_hz,
        )
        values = np.column_stack((left, right)).astype(np.float64, copy=False)
        actual_start = int(np.floor(start_s * sfreq))
        actual_end = min(int(round(self.duration_s * sfreq)), int(round(end_s * sfreq)))
        if values.shape[0] != actual_end - actual_start:
            raise ValueError("FAA source returned sample count inconsistent with its actual bounds")
        return SignalSourceData(
            values_v=values,
            role_to_output={role: channel for role, channel in zip(request.roles, ordered_channels)},
            channel_order=tuple(ordered_channels),
            sfreq_hz=float(sfreq),
            start_sample=actual_start,
            end_sample=actual_end,
            unit="V",
            missing_sample_policy=request.missing_sample_policy,
            montage_id=montage_id,
            montage_fingerprint=montage_fingerprint,
            preprocessing_scope=request.preprocessing_scope,
            preprocessing={"low_hz": request.low_hz, "high_hz": request.high_hz, "notch_hz": request.notch_hz},
            provenance=evidence,
        )

    def load_time_series_source(
        self,
        *,
        start_sample: int,
        end_sample: int,
        channels: list[str],
        expected_sfreq_hz: float,
        roles: list[str],
    ) -> SignalSourceData:
        """Read bounded raw V samples for modules that own preprocessing.

        This boundary deliberately does not call the configured spectral or
        display preprocessing path.  The caller supplies the explicit role
        order; the legacy TNK modules then perform their fixed resampling and
        causal filters themselves.
        """
        total_started = time.perf_counter()
        if len(channels) != len(roles) or len(channels) != 1 or roles != ["Fz"]:
            raise ValueError("time-series source currently requires one explicit Fz channel")
        if not np.isclose(expected_sfreq_hz, self.sfreq_hz, rtol=0.0, atol=1e-12):
            raise ValueError("time-series source sampling rate changed after request planning")
        if start_sample < 0 or end_sample <= start_sample or end_sample > int(round(self.duration_s * self.sfreq_hz)):
            raise ValueError("time-series source sample bounds exceed the recording")
        lookup = {name.casefold(): name for name in self.channel_names}
        try:
            ordered_channels = [lookup[channel.casefold()] for channel in channels]
        except KeyError as exc:
            raise ValueError(f"time-series source channel does not exist: {exc.args[0]}") from exc
        reader_open_ms = 0.0
        if self._access_session is None:
            reader_started = time.perf_counter()
            self._access_session = self._recordings.open_access_session(self._recording)
            reader_open_ms = (time.perf_counter() - reader_started) * 1000
        sample_read_started = time.perf_counter()
        check_cancelled = self.execution_context.check_cancelled if self.execution_context is not None else None
        raw_values = self._access_session.read_samples(
            start_sample, end_sample, ordered_channels, check_cancelled=check_cancelled,
        ).values_v
        sample_read_ms = (time.perf_counter() - sample_read_started) * 1000
        array_started = time.perf_counter()
        values = np.asarray(raw_values, dtype=np.float64)
        array_coercion_ms = (time.perf_counter() - array_started) * 1000
        if values.shape != (end_sample - start_sample, len(channels)):
            raise ValueError("time-series source returned inconsistent sample bounds")
        completed = getattr(self.execution_context, "source_preparation_completed", None)
        if completed is not None:
            completed()
        provenance_started = time.perf_counter()
        provenance = dict(self._recordings.analysis_signal_provenance(self._recording))
        provenance_ms = (time.perf_counter() - provenance_started) * 1000
        provenance.update({
            "time_scope": "exact_requested_absolute_range",
            "requested_range_s": {"start_s": start_sample / self.sfreq_hz, "end_s": end_sample / self.sfreq_hz},
            "actual_range_s": {"start_s": start_sample / self.sfreq_hz, "end_s": end_sample / self.sfreq_hz},
            "channels": ordered_channels, "channel_order": ordered_channels,
            "sfreq_hz": self.sfreq_hz, "signal_input_unit": "V",
            "preprocessing_scope": "module_defined", "preprocessing": "legacy_tnk_owned",
        })
        identity_started = time.perf_counter()
        montage_id, montage_fingerprint = self.signal_source_identity()
        identity_ms = (time.perf_counter() - identity_started) * 1000
        _LOGGER.info(
            "tnk_time_series_source_perf samples=%s:%s count=%s reader_open_ms=%.1f "
            "sample_read_ms=%.1f array_coercion_ms=%.1f provenance_ms=%.1f "
            "identity_ms=%.1f total_ms=%.1f",
            start_sample, end_sample, end_sample - start_sample, reader_open_ms,
            sample_read_ms, array_coercion_ms, provenance_ms, identity_ms,
            (time.perf_counter() - total_started) * 1000,
        )
        return SignalSourceData(
            values_v=values, role_to_output=dict(zip(roles, ordered_channels)),
            channel_order=tuple(ordered_channels), sfreq_hz=self.sfreq_hz,
            start_sample=start_sample, end_sample=end_sample, unit="V",
            missing_sample_policy="reject", montage_id=montage_id,
            montage_fingerprint=montage_fingerprint, preprocessing_scope="module_defined",
            preprocessing={"low_hz": None, "high_hz": None, "notch_hz": None},
            provenance=provenance,
        )
