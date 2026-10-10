"""Independent raw-file, SciPy and NumPy references; no production math imports."""

from __future__ import annotations

import hashlib
import json
import struct
from collections import Counter
from pathlib import Path

import numpy as np
from scipy import signal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


BANDS = {"delta": (1.0, 4.0), "theta": (4.0, 8.0), "alpha": (8.0, 13.0),
         "beta": (13.0, 30.0), "gamma": (30.0, 50.0)}


def source_digests(directory: Path) -> dict[str, str]:
    paths = [directory / name for name in ("manifest.json", "recording-summary.json", "audit.jsonl")]
    paths.extend(sorted(directory.glob("samples-*.bin")))
    result = {}
    for path in paths:
        if path.is_file():
            with path.open("rb") as stream:
                result[path.name] = hashlib.file_digest(stream, "sha256").hexdigest()
    return result


def read_acquisition_sources(directory: Path) -> tuple[np.ndarray, float, list[str]]:
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["EegSignalUnit"] == "V"
    channels = manifest["Channels"]
    header = struct.Struct("<qiiq")
    batches = []
    for path in sorted(directory.glob("samples-*.bin")):
        with path.open("rb") as stream:
            while payload := stream.read(header.size):
                assert len(payload) == header.size, "truncated batch header"
                start, count, channel_count, _ = header.unpack(payload)
                assert start >= 0 and count > 0 and channel_count == len(channels)
                values = np.fromfile(stream, dtype="<f8", count=count * channel_count)
                assert values.size == count * channel_count, "truncated batch samples"
                batches.append((start, values.reshape(count, channel_count)))
    assert batches, "empty raw recording"
    batches.sort(key=lambda item: item[0])
    first = batches[0][0]
    final = max(start + len(values) for start, values in batches)
    data = np.full((final - first, len(channels)), np.nan, dtype=np.float64)
    previous_stop = first
    for start, values in batches:
        assert start >= previous_stop, "overlapping batch sample counters"
        data[start - first:start - first + len(values)] = values
        previous_stop = start + len(values)
    labeled = [row for row in channels if row.get("Label")]
    lookup = {row["Label"].casefold(): row["StreamIndex"] for row in labeled}
    assert len(lookup) == len(labeled), "ambiguous source labels"
    montage = json.loads(manifest["HardwareConfiguration"]["montage_configuration_snapshot_json"])
    outputs, names = [], []
    for row in sorted(montage["DerivedChannels"], key=lambda item: item["DisplayOrder"]):
        positive = data[:, lookup[row["PositiveLabel"].casefold()]]
        negatives = row["NegativeLabels"]
        reference = data[:, [lookup[label.casefold()] for label in negatives]].mean(axis=1) if negatives else 0.0
        outputs.append(positive - reference)
        names.append(row["Name"])
    return np.column_stack(outputs), float(manifest["SamplingRateHz"]), names


def preprocess(values: np.ndarray, sfreq: float, low: float, high: float, notch: float | None) -> np.ndarray:
    # This dataset is finite. Do not guess a gap-filtering policy for other data.
    if not np.isfinite(values).all():
        raise ValueError("independent continuous reference requires a finite recording")
    filtered = values
    if notch is not None:
        b, a = signal.iirnotch(notch, 30.0, fs=sfreq)
        filtered = signal.filtfilt(b, a, filtered, axis=0)
    sos = signal.butter(4, [low, high], btype="bandpass", fs=sfreq, output="sos")
    return signal.sosfiltfilt(sos, filtered, axis=0)


def quality(values: np.ndarray, expected_samples: int) -> dict:
    reasons = []
    if values.ndim != 2 or len(values) != expected_samples:
        reasons.append("missing_samples")
    peak_uv = None
    if not np.isfinite(values).all():
        reasons.append("non_finite")
    else:
        peak_uv = float(np.max(np.abs(values)) * 1e6)
        if peak_uv > float(ANALYSIS_CONTRACT["artifact_peak_uv"]):
            reasons.append("amplitude_threshold")
        if np.any(np.ptp(values, axis=0) * 1e6 < float(ANALYSIS_CONTRACT["flatline_peak_to_peak_uv"])):
            reasons.append("flatline")
        required = max(int(ANALYSIS_CONTRACT["clipping_minimum_samples"]),
                       int(np.ceil(len(values) * float(ANALYSIS_CONTRACT["clipping_minimum_ratio"]))))
        for column in values.T:
            if np.count_nonzero(column == column.min()) >= required or np.count_nonzero(column == column.max()) >= required:
                reasons.append("clipping")
                break
    return {"reasons": reasons, "peak_uv": peak_uv}


def welch(values: np.ndarray, sfreq: float, low: float = 1.0, high: float = 30.0) -> tuple[np.ndarray, np.ndarray | None, dict]:
    count = int(round(float(ANALYSIS_CONTRACT["welch_segment_s"]) * sfreq))
    step = max(1, int(round(count * (1 - float(ANALYSIS_CONTRACT["welch_segment_overlap"])))))
    epochs = [values[start:start + count] for start in range(0, len(values) - count + 1, step)]
    checks = [quality(epoch, count) for epoch in epochs]
    clean = [epoch for epoch, check in zip(epochs, checks) if not check["reasons"]]
    evidence = {
        "clean_segments": len(clean), "total_segments": len(epochs),
        "clean_ratio": len(clean) / len(epochs) if epochs else 0.0,
        "rejected_reasons": sorted({reason for check in checks for reason in check["reasons"]}),
        "rejection_counts": dict(Counter(reason for check in checks for reason in check["reasons"])),
        "peak_uv": max((check["peak_uv"] for check in checks if check["peak_uv"] is not None), default=None),
        "threshold_uv": float(ANALYSIS_CONTRACT["artifact_peak_uv"]),
    }
    full_freqs = np.fft.rfftfreq(count, 1 / sfreq)
    mask = (full_freqs >= low) & (full_freqs <= high)
    if not clean or evidence["clean_ratio"] < float(ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"]):
        return full_freqs[mask], None, evidence
    densities = [signal.periodogram(epoch, fs=sfreq, window="hann", detrend="constant",
                                   scaling="density", axis=0)[1].T for epoch in clean]
    return full_freqs[mask], np.maximum(np.mean(densities, axis=0)[:, mask], 1e-20), evidence


def integrate(freqs: np.ndarray, density: np.ndarray, low: float, high: float, *, interpolate: bool = True) -> np.ndarray:
    assert low < high and low >= freqs[0] and high <= freqs[-1], "uncovered frequency band"
    if not interpolate:
        mask = (freqs >= low) & (freqs <= high)
        return np.trapezoid(density[..., mask], freqs[mask], axis=-1)
    axis = np.concatenate(([low], freqs[(freqs > low) & (freqs < high)], [high]))
    rows = np.asarray(density).reshape(-1, len(freqs))
    powers = np.array([np.trapezoid(np.interp(axis, freqs, row), axis) for row in rows])
    return powers.reshape(density.shape[:-1])


def iapf(freqs: np.ndarray, density: np.ndarray) -> float | None:
    mean = density.mean(axis=0)
    fit = (freqs >= 3) & (freqs <= 30) & ~((freqs >= 7) & (freqs <= 13))
    slope, intercept = np.linalg.lstsq(np.column_stack((np.log10(freqs[fit]), np.ones(fit.sum()))),
                                     np.log10(mean[fit]), rcond=None)[0]
    alpha = (freqs >= 7) & (freqs <= 13)
    baseline = 10 ** (intercept + slope * np.log10(freqs[alpha]))
    residual = np.clip(mean[alpha] - baseline, 0, None)
    index = residual.argmax()
    if residual[index] / max(baseline[index], 1e-20) >= 0.20:
        return float(freqs[alpha][index])
    return float(np.sum(freqs[alpha] * residual) / residual.sum()) if residual.sum() > 0 else None


def shares(freqs: np.ndarray, density: np.ndarray) -> dict[str, float]:
    powers = {name: float(integrate(freqs, density[0], *band)) for name, band in BANDS.items()}
    total = sum(powers.values())
    assert total > 0
    return {name: value / total for name, value in powers.items()}


def metric(algorithm: str, values: np.ndarray, sfreq: float, config: dict) -> tuple[float | dict | None, dict]:
    high = 50.0 if algorithm == "rbp" else 30.0
    freqs, density, evidence = welch(values, sfreq, high=high)
    if density is None:
        return None, evidence
    if algorithm == "rbp":
        return shares(freqs, density), evidence
    if algorithm == "peak_frequency":
        mask = (freqs >= config["low_hz"]) & (freqs <= config["high_hz"])
        return float(freqs[mask][density[0, mask].argmax()]), evidence
    if algorithm == "band_ratio":
        return float(integrate(freqs, density[0], config["numerator_low_hz"], config["numerator_high_hz"]) /
                     integrate(freqs, density[0], config["denominator_low_hz"], config["denominator_high_hz"])), evidence
    peak = iapf(freqs, density)
    if peak is None or algorithm == "iapf":
        return peak, evidence
    theta_band = (max(4.0, peak - 6.0), peak - 2.0)
    if algorithm == "theta_beta":
        return float(integrate(freqs, density[0], *theta_band) /
                     integrate(freqs, density[0], peak + 2.0, 30.0)), evidence
    assert algorithm == "brainbeat"
    # BrainBeat's frozen legacy formula uses inclusive bins, not interpolated edges.
    totals = integrate(freqs, density, 1.0, 30.0, interpolate=False) + 1e-20
    theta = integrate(freqs, density[0], *theta_band, interpolate=False) / totals[0]
    alpha = integrate(freqs, density[1], peak - 2.0, peak + 2.0, interpolate=False) / totals[1]
    return float(theta / (alpha + 1e-20)), evidence


def faa(values: np.ndarray, sfreq: float) -> tuple[float | None, dict]:
    count, step = int(round(2.0 * sfreq)), int(round(sfreq))
    clean, total = [], 0
    for start in range(0, len(values) - count + 1, step):
        epoch = values[start:start + count]
        centered = epoch - epoch.mean(axis=0)
        total += 1
        if np.isfinite(centered).all() and np.max(np.abs(centered)) <= 150e-6:
            clean.append(centered)
    evidence = {"clean_segments": len(clean), "total_segments": total,
                "clean_ratio": len(clean) / total if total else 0.0}
    if len(clean) < 10:
        return None, evidence
    spectra = [signal.periodogram(epoch, fs=sfreq, window="hann", detrend=False,
                                 scaling="density", axis=0)[1].T for epoch in clean]
    frequencies = np.fft.rfftfreq(count, 1 / sfreq)
    powers = integrate(frequencies, np.mean(spectra, axis=0), 8.0, 13.0)
    return float(np.log(powers[1]) - np.log(powers[0])), evidence


def spectrogram(values: np.ndarray, sfreq: float, *, high: float = 50.0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    count, step = int(round(4.0 * sfreq)), int(round(sfreq))
    freqs = np.fft.rfftfreq(count, 1 / sfreq)
    mask = (freqs >= 1.0) & (freqs <= high)
    starts = list(range(0, len(values) - count + 1, step))
    rows = []
    for start in starts:
        epoch = values[start:start + count]
        if quality(epoch, count)["reasons"]:
            rows.append(np.full((values.shape[1], mask.sum()), np.nan))
        else:
            density = signal.periodogram(epoch, fs=sfreq, window="hann", detrend="constant",
                                         scaling="density", axis=0)[1].T
            rows.append(density[:, mask])
    return (np.asarray(starts) + count / 2) / sfreq, freqs[mask], np.stack(rows)
