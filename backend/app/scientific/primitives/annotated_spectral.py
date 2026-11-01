"""Channel-independent Welch measurements with explicit artifact annotations."""

import numpy as np

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.scientific.quality.spectral import evaluate_spectral_window
from .spectral import SpectralEstimate, estimate_welch_psd


def estimate_annotated_channel_psd(data: np.ndarray, sfreq: float, *,
                                   low_hz: float, high_hz: float) -> SpectralEstimate:
    """Keep finite artifact-bearing V samples; reject an invalid channel, not its peers."""
    values = np.asarray(data, dtype=np.float64)
    if values.ndim != 2 or not values.shape[1]:
        raise ValueError("Annotated spectrum requires samples by channels")
    segment_samples = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
    if len(values) < segment_samples:
        raise ValueError("Annotated spectrum requires at least one complete Welch segment")
    grid = np.fft.rfftfreq(segment_samples, 1.0 / sfreq)
    freqs = grid[(grid >= low_hz) & (grid <= high_hz)]
    densities, qualities = [], []
    reasons: list[str] = []
    for index in range(values.shape[1]):
        channel = values[:, index:index + 1]
        check = evaluate_spectral_window(channel, len(values))
        if check.gap.detected:
            densities.append(np.full(len(freqs), np.nan))
            qualities.append({"status": "unavailable", "reasons": list(check.reasons),
                              "gap": check.gap.as_dict(), "peak_uv": check.peak_uv})
            reasons.extend(check.reasons)
            continue
        spectrum = estimate_welch_psd(channel, sfreq, low_hz=low_hz, high_hz=high_hz,
                                      annotate_artifacts=True)
        if spectrum.gate_failed or not np.array_equal(freqs, spectrum.freqs):
            raise ValueError("Annotated channel spectrum has invalid frequency geometry")
        channel_reasons = list(dict.fromkeys((*check.reasons, *spectrum.rejected_reasons)))
        qualities.append({"status": "warning" if channel_reasons else "clean",
                          "reasons": channel_reasons, "peak_uv": check.peak_uv,
                          "clean_segments": spectrum.clean_epochs,
                          "total_segments": spectrum.total_epochs,
                          "clean_ratio": spectrum.signal_quality,
                          "evidence": spectrum.evidence})
        densities.append(spectrum.psd[0])
        reasons.extend(channel_reasons)
    overlap = float(ANALYSIS_CONTRACT["welch_segment_overlap"])
    step = max(1, int(round(segment_samples * (1.0 - overlap))))
    total = len(range(0, len(values) - segment_samples + 1, step))
    clean = min(item.get("clean_segments", 0) for item in qualities)
    return SpectralEstimate(freqs, np.asarray(densities), clean / total, clean, total, None,
        tuple(dict.fromkeys(reasons)), evidence={
            "schema_version": "channel-spectral-quality-v1",
            "quality_policy": "per_channel_annotation",
            "channel_quality": qualities,
            "artifact_peak_uv": ANALYSIS_CONTRACT["artifact_peak_uv"],
            "aggregation": "minimum_channel_clean_ratio",
        })
