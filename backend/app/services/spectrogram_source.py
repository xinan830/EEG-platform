"""Bounded spectrogram source preparation and service response assembly."""

from typing import Any

import numpy as np

from app.models.recording import RecordingSummary
from app.scientific.contracts.spectrogram import (
    SPECTROGRAM_VERSION, STFT_PREPROCESSING_VERSION, stft_context_s,
)
from app.scientific.primitives import ScientificSpectralGateway
from app.scientific.quality import SpectralQualityGateError
from app.services.analysis_input_validation import validate_recording_analysis_input
from app.services.bounded_window_preprocessing import prepare_window


FREQUENCY_BANDS = {
    "delta": (1.0, 4.0), "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0), "beta": (13.0, 30.0),
}


def load_spectrogram(
    recordings: Any,
    gateway: ScientificSpectralGateway,
    recording: RecordingSummary,
    start_s: float = 0.0,
    window_s: float = 30.0,
    channels: list[str] | None = None,
    *,
    low_hz: float = 1.0,
    high_hz: float = 50.0,
    notch_hz: float | None = None,
    output_low_hz: float | None = None,
    output_high_hz: float | None = None,
    execution_context: Any = None,
) -> dict[str, object]:
    """Prepare only target plus context; output density is uV²/Hz."""
    output_low = float(output_low_hz if output_low_hz is not None else low_hz)
    output_high = float(output_high_hz if output_high_hz is not None else high_hz)
    context_s = stft_context_s(float(low_hz))
    if (not all(np.isfinite(v) for v in (high_hz, output_low, output_high, start_s, window_s))
            or high_hz <= low_hz or output_low <= 0 or output_high <= output_low):
        raise ValueError("STFT 频率或时间范围无效")
    if notch_hz is not None and notch_hz not in (50.0, 60.0):
        raise ValueError("陷波仅支持关闭、50Hz 或 60Hz")
    if execution_context is not None:
        execution_context.check_cancelled()
    with recordings.open_access_session(recording, verify_content_hash=False) as session:
        sfreq = float(session.sampling_rate_hz)
        selected = list(session.channel_names) if channels is None else channels
        validate_recording_analysis_input(
            session.sample_count / sfreq, sfreq, session.channel_names,
            float(start_s), float(start_s + window_s), selected,
            frequency_range=(float(low_hz), max(float(high_hz), output_high)),
        )
        if notch_hz is not None and notch_hz >= sfreq / 2:
            raise ValueError("陷波频率必须低于奈奎斯特频率")
        start_index = int(np.floor(float(start_s) * sfreq))
        stop_index = min(session.sample_count, start_index + int(round(float(window_s) * sfreq)))
        if stop_index - start_index < int(round(4.0 * sfreq)):
            raise ValueError("频谱分析窗口至少需要 4 秒")
        prepared = prepare_window(
            session, start_sample=start_index, end_sample=stop_index,
            channels=channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            context_s=context_s, algorithm_version=STFT_PREPROCESSING_VERSION,
            execution_context=execution_context,
        )
    names = list(prepared.channel_names)
    actual_start = start_index / sfreq
    # Preserve the existing aggregate Welch gate before per-row STFT quality.
    spectrum = gateway.welch(prepared.data, sfreq, low_hz=low_hz,
                             high_hz=min(max(high_hz, output_high, 30.0),
                                         np.nextafter(sfreq / 2.0, 0.0)))
    if spectrum.gate_failed:
        raise SpectralQualityGateError({
            "clean_segments": spectrum.clean_epochs, "total_segments": spectrum.total_epochs,
            "clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed,
            "rejected_reasons": list(spectrum.rejected_reasons),
            "evidence": {**spectrum.evidence, "preprocessing": prepared.evidence},
        })
    # Integrate standard bands before cropping the returned frequency axis.
    times, freqs, values, quality = gateway.spectrogram(
        prepared.data, sfreq,
        low_hz=min(output_low, min(edges[0] for edges in FREQUENCY_BANDS.values())),
        high_hz=min(max(output_high, max(edges[1] for edges in FREQUENCY_BANDS.values())),
                    np.nextafter(sfreq / 2.0, 0.0)),
    )
    frequency_mask = (freqs >= output_low) & (freqs <= output_high)
    if not np.any(frequency_mask):
        raise ValueError("请求的 STFT 频率范围没有可用频率点")
    power_uv = values[:, :, frequency_mask] * 1e12
    power_db = 10.0 * np.log10(np.maximum(power_uv, np.finfo(float).tiny))
    linear_values = {name: power_uv[:, index, :].tolist() for index, name in enumerate(names)}
    db_values = {name: power_db[:, index, :].tolist() for index, name in enumerate(names)}
    band_series = {
        name: {
            band: [
                (float(gateway.integrate_band(freqs, row, low, high) * 1e12)
                 if np.isfinite(row).all() else float("nan"))
                if low >= freqs[0] and high <= freqs[-1] else None
                for row in values[:, index, :]
            ]
            for band, (low, high) in FREQUENCY_BANDS.items()
        }
        for index, name in enumerate(names)
    }
    for item in quality:
        item["center_s"] = float(item["center_s"]) + actual_start
        item["start_s"] = float(item["start_s"]) + actual_start
        item["end_s"] = float(item["end_s"]) + actual_start
    if execution_context is not None:
        execution_context.check_cancelled()
    source = recordings.analysis_signal_provenance(recording)
    return {
        "recording_id": recording.id,
        "window_start_s": actual_start,
        "window_duration_s": (stop_index - start_index) / sfreq,
        "sfreq_hz": float(sfreq),
        "channels": names,
        "analysis_reference": source["analysis_reference"],
        "signal_source": source,
        "times_s": (times + actual_start).tolist(),
        "frequencies_hz": freqs[frequency_mask].tolist(),
        "power": linear_values,
        "power_linear": linear_values,
        "power_db": db_values,
        "band_power_timeseries": band_series,
        "units": "uV^2/Hz",
        "power_linear_units": "uV^2/Hz",
        "power_db_units": "dB re 1 uV^2/Hz",
        "band_power_timeseries_units": "uV^2",
        "analysis_algorithm_version": STFT_PREPROCESSING_VERSION,
        "spectrogram_contract_version": SPECTROGRAM_VERSION,
        "algorithm_version": SPECTROGRAM_VERSION,
        "frequency_range_hz": {"low_hz": output_low, "high_hz": output_high},
        "filter_frequency_range_hz": {"low_hz": float(low_hz), "high_hz": float(high_hz)},
        "notch_hz": notch_hz,
        "preprocessing": prepared.evidence,
        "segment_s": 4.0,
        "step_s": 1.0,
        "quality": {
            "windows": quality,
            "clean_windows": sum(item["status"] == "clean" for item in quality),
            "total_windows": len(quality),
            "bad_windows": sum(item["status"] == "bad" for item in quality),
            "evidence": {
                "schema_version": "spectrogram-window-evidence-v1",
                "transform_padding": {"used": False, "kind": "none", "samples": 0},
                "gap_detected_windows": sum(bool(item["gap"]["detected"]) for item in quality),
            },
        },
    }
