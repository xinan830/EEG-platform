"""独立复算阅图滤波采样点；禁止导入 app.services 中的生产算法。"""

from __future__ import annotations

import argparse
from pathlib import Path

import mne
import numpy as np
from scipy import signal


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="独立复算 BDF/EDF 指定时间点的显示电压")
    parser.add_argument("path", type=Path)
    parser.add_argument("--time", type=float, required=True)
    parser.add_argument("--channels", default="F3,Fz,F4,Pz,Oz")
    parser.add_argument("--low", type=float, default=0.5)
    parser.add_argument("--high", type=float, default=70.0)
    parser.add_argument("--notch", type=float)
    parser.add_argument("--baseline-stabilization", action="store_true")
    parser.add_argument("--montage", choices=("original", "average"), default="original")
    parser.add_argument("--average-exclude", default="")
    return parser.parse_args()


def _read_raw(path: Path):
    reader = mne.io.read_raw_bdf if path.suffix.lower() == ".bdf" else mne.io.read_raw_edf
    return reader(path, preload=False, verbose=False)


def _resolve(names: list[str], requested: list[str]) -> list[int]:
    lookup = {name.upper(): index for index, name in enumerate(names)}
    missing = [name for name in requested if name.upper() not in lookup]
    if missing:
        raise ValueError(f"文件中不存在通道：{', '.join(missing)}")
    return [lookup[name.upper()] for name in requested]


def _filter(data: np.ndarray, sfreq: float, low: float, high: float, notch: float | None, baseline: bool) -> np.ndarray:
    x = np.asarray(data, dtype=float).T
    if baseline:
        offset = x[:, 0].copy()
        centered = np.empty_like(x)
        for index in range(x.shape[1]):
            offset += 0.01 * (x[:, index] - offset)
            centered[:, index] = x[:, index] - offset
        x = centered
    if notch is not None:
        notch_sos = signal.tf2sos(*signal.iirnotch(notch, 30.0, sfreq))
        first = x[:, 0]
        zi = signal.sosfilt_zi(notch_sos)[:, None, :] * first[None, :, None]
        x, _ = signal.sosfilt(notch_sos, x, axis=-1, zi=zi)
    band_sos = signal.butter(4, [low, high], btype="bandpass", fs=sfreq, output="sos")
    first = x[:, 0]
    zi = signal.sosfilt_zi(band_sos)[:, None, :] * first[None, :, None]
    x, _ = signal.sosfilt(band_sos, x, axis=-1, zi=zi)
    return x.T


def main() -> None:
    args = _arguments()
    raw = _read_raw(args.path)
    try:
        sfreq = float(raw.info["sfreq"])
        sample = int(args.time * sfreq)
        if sample < 0 or sample >= raw.n_times:
            raise ValueError("时间点超出文件范围")
        all_names = list(raw.ch_names)
        data = raw.get_data(start=0, stop=sample + 1).T
        filtered = _filter(data, sfreq, args.low, args.high, args.notch, args.baseline_stabilization)
        requested = [item.strip() for item in args.channels.split(",") if item.strip()]
        requested_indices = _resolve(all_names, requested)
        values = filtered[-1]
        if args.montage == "average":
            excluded = {item.strip().upper() for item in args.average_exclude.split(",") if item.strip()}
            participants = [index for index, name in enumerate(all_names) if name.upper() not in excluded]
            if not participants:
                raise ValueError("平均参考没有参与通道")
            values = values - float(np.mean(values[participants]))
        print(f"sample={sample} time={sample / sfreq:.6f}s sfreq={sfreq:g}Hz montage={args.montage}")
        for name, index in zip(requested, requested_indices):
            print(f"{name}\t{values[index] * 1e6:.6f} uV")
    finally:
        raw.close()


if __name__ == "__main__":
    main()
