"""Stateful ANT-derived Fz metrics over canonical V sample blocks."""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import AlgorithmFailure, AlgorithmResult

from .legacy_dsp import CascadedFilter, MovingAverage, QualityGate, StreamingResampler, fft_magnitudes, zenlite_band_amplitudes, zenlite_relaxation_raw
from .legacy_models import CrimsonModel, LegacyModelError, ZenliteModel

_ASSET_DIR = Path(__file__).resolve().parent / "assets"


def _gain(unit: str) -> float:
    normalized = str(unit).strip().lower().replace("µ", "u").replace("μ", "u")
    if normalized in {"v", "volt", "volts"}:
        return 1e6
    if normalized in {"uv", "microvolt", "microvolts"}:
        return 1.0
    raise ValueError("Fz 数据单位必须明确为 V 或 µV")


def _result(metric: str, value: float | None, *, start_s: float, end_s: float,
            quality: str, failure: AlgorithmFailure | None, evidence: dict[str, Any]) -> AlgorithmResult:
    return AlgorithmResult(
        value=value, unit="score", channel="Fz", requested_range={"start_s": start_s, "end_s": end_s},
        actual_range={"start_s": start_s, "end_s": end_s}, quality=quality, failure=failure,
        evidence={"extensions": {"tnk_evidence": {"algorithm_id": metric, **evidence}}},
    )


class CrimsonEngine:
    metric_id = "tnk_crimson_meditation"

    def __init__(self, sfreq_hz: float, *, input_unit: str = "V", model: Any = None) -> None:
        self.sfreq_hz = float(sfreq_hz)
        self._gain = _gain(input_unit)
        self._resampler = StreamingResampler(self.sfreq_hz, 250)
        self._filter = CascadedFilter(250, (("bandpass", 2, 45), ("bandstop", 58, 62), ("bandstop", 48, 52)))
        self._quality = QualityGate(low_frequency_count=12)
        self._smooth = MovingAverage(20)
        self._model_error = ""
        self._model = model
        self._history = np.empty(0, dtype=np.float32)
        self._packet_buffer = np.empty(0, dtype=np.float32)
        self._packet_count = 0
        self._processed_samples = 0
        self._last_raw = None
        self._last_score = None
        if self._model is None:
            try:
                self._model = CrimsonModel(_ASSET_DIR / "cmsn_meditation.tflite")
            except Exception as exc:  # optional runtime dependency or asset mismatch
                self._model_error = str(exc)

    def reset(self) -> None:
        self._resampler.reset()
        self._filter.reset()
        self._quality.reset()
        self._smooth.reset()
        self._history = np.empty(0, dtype=np.float32)
        self._packet_buffer = np.empty(0, dtype=np.float32)
        self._packet_count = 0
        self._processed_samples = 0
        self._last_raw = None
        self._last_score = None

    def run(self, fz_values_v: np.ndarray, *, start_s: float, end_s: float) -> AlgorithmResult:
        self.reset()
        return self.process_chunk(fz_values_v, start_s=start_s, end_s=end_s)

    def process_chunk(self, fz_values_v: np.ndarray, *, start_s: float, end_s: float) -> AlgorithmResult:
        try:
            values = np.asarray(fz_values_v, dtype=np.float64)
            if values.ndim != 1 or not np.all(np.isfinite(values)):
                raise ValueError("Fz 原始时序必须是一维有限 V 数组")
            filtered = self._filter.process(self._resampler.process(values * self._gain))
            self._processed_samples += len(filtered)
            self._packet_buffer = np.concatenate((self._packet_buffer, filtered))
            complete_count = len(self._packet_buffer) // 50 * 50
            packets = self._packet_buffer[:complete_count].reshape(-1, 50)
            self._packet_buffer = self._packet_buffer[complete_count:]
            for packet in packets:
                self._packet_count += 1
                if self._quality.update(self._quality.amplitude_bad(packet)):
                    continue
                self._history = np.concatenate((self._history, packet))[-1250:]
                if len(self._history) < 1250 or self._packet_count % 5:
                    continue
                if self._quality.update(self._quality.frequency_bad(self._history)):
                    continue
                try:
                    self._last_raw = float(np.clip(self._model.predict(self._history)[2], 0, 1) * 100)
                except Exception as exc:
                    return _result(self.metric_id, None, start_s=start_s, end_s=end_s, quality="unavailable",
                                   failure=AlgorithmFailure(code="TNK_MODEL_INFERENCE_FAILED", message=str(exc)),
                                   evidence={"model_sfreq_hz": 250, "window_samples": 1250})
                self._last_score = self._smooth.add(self._last_raw)
            if self._processed_samples < 1250:
                return _result(self.metric_id, None, start_s=start_s, end_s=end_s, quality="insufficient_data",
                                failure=AlgorithmFailure(code="TNK_WARMUP", message="Crimson 需要 5 秒 Fz 窗口"),
                                evidence={"model_sfreq_hz": 250, "window_samples": int(self._processed_samples)})
            if self._model_error:
                return _result(self.metric_id, None, start_s=start_s, end_s=end_s, quality="unavailable",
                               failure=AlgorithmFailure(code="TNK_MODEL_UNAVAILABLE", message=self._model_error),
                               evidence={"model": "cmsn_meditation.tflite", "sha256": CrimsonModel.SHA256})
            if self._last_score is None:
                return _result(self.metric_id, None, start_s=start_s, end_s=end_s, quality="gate_failed",
                               failure=AlgorithmFailure(code="TNK_QUALITY_REJECTED", message="Crimson Fz 窗口未通过质量门"),
                               evidence={"model_sfreq_hz": 250, "window_samples": 1250})
            return _result(self.metric_id, self._last_score, start_s=start_s, end_s=end_s, quality="clean",
                           failure=None, evidence={"raw_score": self._last_raw, "model_sfreq_hz": 250, "window_samples": 1250,
                                                   "smoothing": "moving_average_20"})
        except (ValueError, LegacyModelError) as exc:
            return _result(self.metric_id, None, start_s=start_s, end_s=end_s, quality="unavailable",
                           failure=AlgorithmFailure(code="TNK_LEGACY_INPUT_INVALID", message=str(exc)), evidence={})


class ZenliteEngine:
    metric_ids = ("tnk_zenlite_calmness", "tnk_zenlite_awareness", "tnk_zenlite_spectral_relaxation")

    def __init__(self, sfreq_hz: float, *, input_unit: str = "V", model: Any = None) -> None:
        self.sfreq_hz = float(sfreq_hz)
        self._gain = _gain(input_unit)
        self._resampler = StreamingResampler(self.sfreq_hz, 256, allow_upsampling=True)
        self._filter = CascadedFilter(256, (("bandpass", 2, 45), ("bandstop", 59, 61), ("bandstop", 49, 51)))
        self._quality = QualityGate(low_frequency_count=11)
        self._calm, self._aware, self._relax = MovingAverage(10), MovingAverage(10), MovingAverage(10)
        self._history = np.empty(0, dtype=np.float32)
        self._packet_buffer = np.empty(0, dtype=np.float32)
        self._packet_count = 0
        self._processed_samples = 0
        self._last_raw_relax = None
        self._last_relax = None
        self._last_model_raw = None
        self._good_window_seen = False
        self._model_error = ""
        self._model = model
        if self._model is None:
            try:
                self._model = ZenliteModel(_ASSET_DIR / "zenlite-meditation.tflite")
            except Exception as exc:
                self._model_error = str(exc)

    def reset(self) -> None:
        self._resampler.reset()
        self._filter.reset()
        self._quality.reset()
        self._calm.reset()
        self._aware.reset()
        self._relax.reset()
        self._history = np.empty(0, dtype=np.float32)
        self._packet_buffer = np.empty(0, dtype=np.float32)
        self._packet_count = 0
        self._processed_samples = 0
        self._last_raw_relax = None
        self._last_relax = None
        self._last_model_raw = None
        self._good_window_seen = False

    def run(self, fz_values_v: np.ndarray, *, start_s: float, end_s: float) -> dict[str, AlgorithmResult]:
        self.reset()
        return self.process_chunk(fz_values_v, start_s=start_s, end_s=end_s)

    def process_chunk(self, fz_values_v: np.ndarray, *, start_s: float, end_s: float) -> dict[str, AlgorithmResult]:
        base = {metric: _result(metric, None, start_s=start_s, end_s=end_s, quality="unavailable",
                                failure=None, evidence={}) for metric in self.metric_ids}
        try:
            values = np.asarray(fz_values_v, dtype=np.float64)
            if values.ndim != 1 or not np.all(np.isfinite(values)):
                raise ValueError("Fz 原始时序必须是一维有限 V 数组")
            filtered = self._filter.process(self._resampler.process(values * self._gain))
            self._processed_samples += len(filtered)
            self._packet_buffer = np.concatenate((self._packet_buffer, filtered))
            complete_count = len(self._packet_buffer) // 30 * 30
            packets = self._packet_buffer[:complete_count].reshape(-1, 30)
            self._packet_buffer = self._packet_buffer[complete_count:]
            for packet in packets:
                self._packet_count += 1
                if self._quality.update(self._quality.amplitude_bad(packet)):
                    continue
                self._history = np.concatenate((self._history, packet))[-1200:]
                if len(self._history) < 1200:
                    continue
                magnitudes = fft_magnitudes(self._history)
                if self._quality.update(self._quality.frequency_bad(magnitudes)):
                    continue
                self._good_window_seen = True
                if self._packet_count % 8 == 0:
                    raw_relax = zenlite_relaxation_raw(zenlite_band_amplitudes(magnitudes))
                    if raw_relax is not None:
                        self._last_raw_relax = raw_relax
                        self._last_relax = self._relax.add(raw_relax)
            if self._processed_samples < 1200:
                failure = AlgorithmFailure(code="TNK_WARMUP", message="Zenlite 需要约 4.6875 秒 Fz 窗口")
                return {metric: _result(metric, None, start_s=start_s, end_s=end_s, quality="insufficient_data", failure=failure, evidence={"model_sfreq_hz": 256, "window_samples": int(self._processed_samples)}) for metric in self.metric_ids}
            if self._last_relax is None:
                base["tnk_zenlite_spectral_relaxation"] = _result("tnk_zenlite_spectral_relaxation", None, start_s=start_s, end_s=end_s, quality="unavailable", failure=AlgorithmFailure(code="TNK_SPECTRUM_INVALID", message="频谱放松度频带功率无效"), evidence={})
            else:
                base["tnk_zenlite_spectral_relaxation"] = _result("tnk_zenlite_spectral_relaxation", self._last_relax, start_s=start_s, end_s=end_s, quality="clean", failure=None, evidence={"raw_score": self._last_raw_relax, "smoothing": "moving_average_10", "model_sfreq_hz": 256})
            if self._model_error:
                failure = AlgorithmFailure(code="TNK_MODEL_UNAVAILABLE", message=self._model_error)
                for metric in ("tnk_zenlite_calmness", "tnk_zenlite_awareness"):
                    base[metric] = _result(metric, None, start_s=start_s, end_s=end_s, quality="unavailable", failure=failure, evidence={"model": "zenlite-meditation.tflite", "sha256": ZenliteModel.SHA256})
                return base
            # The model is evaluated on the final canonical 1 s tick.  The
            # spectral stream above still advances every packet so quality and
            # relaxation state retain the old update order.
            if len(self._history) < 1200:
                raise ValueError("Zenlite 没有可用模型窗口")
            if self._good_window_seen:
                try:
                    self._last_model_raw = np.clip(self._model.predict(self._history), 0, 1).astype(np.float32) * np.float32(100)
                except Exception as exc:
                    failure = AlgorithmFailure(code="TNK_MODEL_INFERENCE_FAILED", message=str(exc))
                    for metric in ("tnk_zenlite_calmness", "tnk_zenlite_awareness"):
                        base[metric] = _result(metric, None, start_s=start_s, end_s=end_s, quality="unavailable", failure=failure, evidence={})
                    return base
            if self._last_model_raw is None:
                raise ValueError("Zenlite 没有可用模型窗口")
            calm, aware = self._calm.add(self._last_model_raw[0]), self._aware.add(self._last_model_raw[1])
            base["tnk_zenlite_calmness"] = _result("tnk_zenlite_calmness", calm, start_s=start_s, end_s=end_s, quality="clean", failure=None, evidence={"raw_score": float(self._last_model_raw[0]), "smoothing": "moving_average_10", "model_sfreq_hz": 256})
            base["tnk_zenlite_awareness"] = _result("tnk_zenlite_awareness", aware, start_s=start_s, end_s=end_s, quality="clean", failure=None, evidence={"raw_score": float(self._last_model_raw[1]), "smoothing": "moving_average_10", "model_sfreq_hz": 256})
            return base
        except (ValueError, LegacyModelError) as exc:
            failure = AlgorithmFailure(code="TNK_LEGACY_INPUT_INVALID", message=str(exc))
            return {metric: _result(metric, None, start_s=start_s, end_s=end_s, quality="unavailable", failure=failure, evidence={}) for metric in self.metric_ids}

