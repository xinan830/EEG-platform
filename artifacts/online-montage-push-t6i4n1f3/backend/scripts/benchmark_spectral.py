"""Repeatable local benchmark for the frozen offline spectral pipeline.

This is a diagnostic script only: it generates deterministic synthetic EEG and
never writes recordings or changes production configuration.
"""
from __future__ import annotations

import time
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.scientific.primitives.spectral import estimate_spectrogram_with_quality, preprocess_offline


def main() -> None:
    sfreq = 500.0
    seconds = 10 * 60
    channels = 21
    samples = int(seconds * sfreq)
    rng = np.random.default_rng(42)
    t = np.arange(samples, dtype=float) / sfreq
    data = 15e-6 * np.sin(2 * np.pi * 10 * t[:, None]) + rng.normal(0, 2e-6, (samples, channels))
    started = time.perf_counter()
    filtered = preprocess_offline(data, sfreq)
    preprocess_ms = (time.perf_counter() - started) * 1000
    started = time.perf_counter()
    centers, freqs, power, quality = estimate_spectrogram_with_quality(filtered[:30 * int(sfreq)], sfreq)
    spectrogram_ms = (time.perf_counter() - started) * 1000
    print(f"preprocess: {preprocess_ms:.1f} ms for {seconds:g}s × {channels} channels")
    print(f"spectrogram: {spectrogram_ms:.1f} ms; matrix={len(centers)}×{len(freqs)}×{channels}; bad_windows={sum(item['status'] == 'bad' for item in quality)}")


if __name__ == "__main__":
    main()
