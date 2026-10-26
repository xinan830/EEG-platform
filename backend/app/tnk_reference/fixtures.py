"""Deterministic, identity-free analytic signals for TNK reference tests."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np


def generate_analytic_fixture(manifest_path: Path) -> tuple[np.ndarray, np.ndarray, list[str]]:
    """Create the frozen samples-by-channels V fixture from its manifest."""
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    fixture = manifest["fixture"]
    sfreq = float(fixture["sampling_rate_hz"])
    count = int(round(sfreq * fixture["duration_s"]))
    time_s = np.arange(count, dtype=np.float64) / sfreq
    channels = list(fixture["channel_order"])
    formulas = fixture["channel_formulas"]
    signals = {
        "Fz": 20e-6 * np.sin(2 * np.pi * 6 * time_s) + 10e-6 * np.sin(2 * np.pi * 20 * time_s),
        "Pz": 20e-6 * np.sin(2 * np.pi * 10 * time_s) + 5e-6 * np.sin(2 * np.pi * 6 * time_s),
        "Oz": 15e-6 * np.sin(2 * np.pi * 10 * time_s) + 8e-6 * np.sin(2 * np.pi * 2 * time_s),
        "F3": 10e-6 * np.sin(2 * np.pi * 10 * time_s),
        "F4": 20e-6 * np.sin(2 * np.pi * 10 * time_s),
    }
    if set(formulas) != set(channels) or set(signals) != set(channels):
        raise ValueError("analytic fixture channel/formula sets disagree")
    values_v = np.column_stack([signals[name] for name in channels]).astype(np.float64, copy=False)
    return time_s, values_v, channels


def faa_report(values_v: np.ndarray, sfreq_hz: float, *, discard_s: float = 12.0,
               cap_s: float = 1800.0) -> dict[str, object]:
    """Apply source FAA report bounds before paired-epoch calculation."""
    values = np.asarray(values_v, dtype=np.float64)
    if values.ndim != 2 or values.shape[1] != 2 or not np.isfinite(values).all():
        raise ValueError("FAA report expects finite samples-by-F3/F4 input")
    if not np.isfinite(sfreq_hz) or sfreq_hz <= 0 or discard_s < 0 or cap_s <= 0:
        raise ValueError("FAA report timing configuration is invalid")
    start = int(round(discard_s * sfreq_hz))
    stop = min(len(values), int(round(cap_s * sfreq_hz)))
    if stop <= start:
        return {"faa": None, "reason": "too_short", "sample_bounds": [start, stop],
                "total_epochs": 0, "clean_epochs": 0}
    from .metrics import faa
    from .primitives import faa_epoch_psd

    prepared = faa_epoch_psd(values[start:stop], sfreq_hz)
    result = faa(prepared["psd"], prepared["frequencies_hz"], clean_epochs=prepared["clean_epochs"])
    return {**result, "total_epochs": prepared["total_epochs"],
            "clean_epochs": prepared["clean_epochs"], "sample_bounds": [start, stop],
            "reason": prepared["reason"] or result["reason"]}
