"""独立 Reference 实现：不调用项目频谱函数，输出 PSD 与频段功率。"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import mne
import numpy as np
from scipy import signal


def integrate(freqs: np.ndarray, psd: np.ndarray, low: float, high: float) -> float:
    points = np.concatenate(([low], freqs[(freqs > low) & (freqs < high)], [high]))
    values = np.interp(points, freqs, psd)
    return float(np.trapezoid(values, points))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("file", type=Path)
    parser.add_argument("--start-s", type=float, default=0.0)
    parser.add_argument("--window-s", type=float, default=30.0)
    parser.add_argument("--channels", nargs="+", required=True)
    args = parser.parse_args()

    raw = mne.io.read_raw_bdf(args.file, preload=True, verbose=False)
    start = int(np.floor(args.start_s * raw.info["sfreq"]))
    stop = min(raw.n_times, start + int(round(args.window_s * raw.info["sfreq"])))
    names = [next(name for name in raw.ch_names if name.casefold() == wanted.casefold()) for wanted in args.channels]
    data = raw.get_data(picks=names, start=start, stop=stop).T
    sos = signal.butter(4, [1.0, 30.0], btype="bandpass", fs=raw.info["sfreq"], output="sos")
    filtered = signal.sosfiltfilt(sos, data, axis=0)
    freqs, psd = signal.welch(filtered, fs=raw.info["sfreq"], window="hann", nperseg=int(4 * raw.info["sfreq"]), noverlap=int(2 * raw.info["sfreq"]), detrend="constant", scaling="density", axis=0)
    mask = (freqs >= 1.0) & (freqs <= 30.0)
    freqs, psd = freqs[mask], psd[mask].T
    bands = {"delta": [1.0, 4.0], "theta": [4.0, 8.0], "alpha": [8.0, 13.0], "beta": [13.0, 30.0]}
    power = {name: {band: integrate(freqs, row, *edges) * 1e12 for band, edges in bands.items()} for name, row in zip(names, psd)}
    for values in power.values():
        total = sum(values.values())
        values.update({f"{band}_rbp": value / total if total else 0.0 for band, value in list(values.items())})
    digest = hashlib.sha256(args.file.read_bytes()).hexdigest()
    print(json.dumps({"file": str(args.file), "sha256": digest, "start_s": args.start_s, "window_s": (stop - start) / raw.info["sfreq"], "sfreq_hz": raw.info["sfreq"], "channels": names, "algorithm_version": "reference-v1", "frequencies_hz": freqs.tolist(), "power": power}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
