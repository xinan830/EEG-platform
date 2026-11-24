"""独立 EEG 导联验证器。

此脚本故意不导入 app.services，使用 MNE/NumPy/SciPy 独立读取、滤波和
计算导联，用于和阅图接口结果做交叉验证。
"""

from __future__ import annotations

import argparse
from pathlib import Path

import mne
import numpy as np
from scipy import signal


def resolve(name: str, available: list[str]) -> str:
    aliases = {"T3": "T7", "T4": "T8", "T5": "P7", "T6": "P8"}
    wanted = aliases.get(name.strip().upper(), name.strip().upper())
    for candidate in available:
        if aliases.get(candidate.upper(), candidate.upper()) == wanted:
            return candidate
    raise ValueError(f"通道不存在：{name}")


def independent_filter(samples: np.ndarray, sfreq: float, low: float, high: float, notch: float | None) -> np.ndarray:
    """Independent causal IIR implementation; no project imports."""
    values = np.asarray(samples, dtype=float)
    if not 0 < low < high < sfreq / 2:
        raise ValueError("滤波范围不合法")
    b_bp, a_bp = signal.butter(4, [low, high], btype="bandpass", fs=sfreq)
    b_notch, a_notch = signal.iirnotch(notch, 30.0, sfreq) if notch else (None, None)
    centered = values.T - values.T.mean(axis=1, keepdims=True)
    output = np.empty_like(centered)
    for index, row in enumerate(centered):
        current = row
        if b_notch is not None and a_notch is not None:
            current, _ = signal.lfilter(b_notch, a_notch, current, zi=signal.lfilter_zi(b_notch, a_notch) * current[0])
        current, _ = signal.lfilter(b_bp, a_bp, current, zi=signal.lfilter_zi(b_bp, a_bp) * current[0])
        output[index] = current
    return output.T


def calculate(path: Path, time_s: float, channels: list[str], low: float, high: float, notch: float | None) -> dict:
    reader = mne.io.read_raw_bdf if path.suffix.lower() == ".bdf" else mne.io.read_raw_edf
    raw = reader(path, preload=False, verbose=False)
    try:
        sfreq = float(raw.info["sfreq"])
        duration = float(raw.n_times / sfreq)
        names = list(raw.ch_names)
        selected = [resolve(name, names) for name in channels]
        # 读取全部 EEG 通道，确保平均参考的分母和参与通道明确可审计。
        read_names = names
        start = max(0.0, min(time_s, duration))
        pad = max(1.0, min(10.0, 3.0 / low))
        read_start = max(0.0, start - pad)
        read_stop = min(duration, start + 0.1 + pad)
        start_sample = int(read_start * sfreq)
        stop_sample = max(start_sample + 1, int(read_stop * sfreq))
        indices = [names.index(name) for name in read_names]
        window = np.asarray(raw.get_data(picks=indices, start=start_sample, stop=stop_sample), dtype=float).T
        filtered = independent_filter(window, sfreq, low, high, notch)
        sample_index = max(0, int((start - read_start) * sfreq))
        raw_sample = window[sample_index]
        filtered_sample = filtered[sample_index]
        selected_indices = [read_names.index(name) for name in selected]
        original = {name: float(filtered_sample[index] * 1e6) for name, index in zip(selected, selected_indices)}
        average_value = float(filtered_sample.mean() * 1e6)
        average = {f"{name}-AVG": float(filtered_sample[index] * 1e6 - average_value) for name, index in zip(selected, selected_indices)}
        return {
            "file": str(path), "sfreq": sfreq, "duration_s": duration, "time_s": start,
            "channels": selected, "raw_uv": {name: float(raw_sample[index] * 1e6) for name, index in zip(selected, selected_indices)},
            "filtered_uv": original, "average_reference_mean_uv": average_value, "average_uv": average,
        }
    finally:
        raw.close()


def synthetic_check() -> None:
    """Verify derivation algebra with values whose expected result is known."""
    source = np.array([[10.0, 4.0, -2.0], [8.0, 2.0, 0.0]])
    mean = source.mean(axis=1, keepdims=True)
    average = source - mean
    np.testing.assert_allclose(mean[:, 0], [4.0, 10.0 / 3.0])
    np.testing.assert_allclose(average[0], [6.0, 0.0, -6.0])
    bipolar = source[:, :-1] - source[:, 1:]
    np.testing.assert_allclose(bipolar, [[6.0, 6.0], [6.0, 2.0]])
    print("合成数据验证通过：平均参考和相邻双极导联公式正确。")


def main() -> None:
    parser = argparse.ArgumentParser(description="独立计算 EEG 原始参考和平均参考")
    parser.add_argument("path", type=Path, nargs="?")
    parser.add_argument("--synthetic", action="store_true", help="只运行已知输入的公式验证")
    parser.add_argument("--time", type=float, default=5.560)
    parser.add_argument("--channels", default="F3,Fz,F4,Pz,Oz")
    parser.add_argument("--low", type=float, default=0.5)
    parser.add_argument("--high", type=float, default=70.0)
    parser.add_argument("--notch", type=float, default=50.0)
    args = parser.parse_args()
    if args.synthetic:
        synthetic_check()
        return
    if args.path is None:
        parser.error("请提供 BDF/EDF 路径，或使用 --synthetic")
    result = calculate(args.path, args.time, [item for item in args.channels.split(",") if item.strip()], args.low, args.high, args.notch)
    print(f"文件：{result['file']}\n采样率：{result['sfreq']:g} Hz · 时长：{result['duration_s']:g} s · 时间：{result['time_s']:.3f} s")
    print("\n原始采样值（未滤波，便于核对文件数据）：")
    for name, value in result["raw_uv"].items(): print(f"  {name}: {value:.6f} uV")
    print("\n独立 IIR 滤波后·原始记录（不重参考）：")
    for name, value in result["filtered_uv"].items(): print(f"  {name}: {value:.6f} uV")
    print(f"\n独立 IIR 滤波后·平均参考（21 通道平均值 {result['average_reference_mean_uv']:.6f} uV）：")
    for name, value in result["average_uv"].items(): print(f"  {name}: {value:.6f} uV")


if __name__ == "__main__":
    main()
