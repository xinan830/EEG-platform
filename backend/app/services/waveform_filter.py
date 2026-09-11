"""可复现的阅图显示滤波器和状态检查点。"""

from __future__ import annotations

import numpy as np
from scipy import signal


FilterState = tuple[np.ndarray | None, list[np.ndarray] | None, list[np.ndarray]]


DEFAULT_DISPLAY_FILTERS = {
    "low_cut_hz": 0.5,
    "high_cut_hz": 70.0,
    "notch_hz": None,
    "baseline_stabilization": False,
}

# 静态窗口和 WebSocket 播放必须共用这些值，修改时同步更新回归黄金值。
DISPLAY_FILTER_CONTRACT = {
    "algorithm_version": "display-iir-sos-v2",
    "phase": "causal",
    "coefficient_form": "second_order_sections",
    "state_initialization": "sosfilt_zi_scaled_by_first_sample",
    "baseline_stabilization_default": False,
    "baseline_type": "sample_ema",
    "baseline_alpha": 0.01,
    "notch_type": "iir_notch",
    "notch_q": 30.0,
    "bandpass_type": "butterworth",
    "bandpass_prototype_order": 4,
    "bandpass_digital_order": 8,
    "chunk_seconds": 0.05,
}


class DisplaySignalFilter:
    """按固定契约进行因果、可分块的显示滤波；绝不改写导入文件。"""

    def __init__(self, sfreq: float, channel_count: int, notch_freq: float | None = None, bp_low: float = 0.5, bp_high: float = 70.0, baseline_stabilization: bool = False):
        self.sfreq = float(sfreq)
        self.channel_count = int(channel_count)
        if self.channel_count <= 0:
            raise ValueError("至少需要一个 EEG 通道")
        if not 0 < float(bp_low) < float(bp_high) < self.sfreq / 2:
            raise ValueError("低切必须小于高切，且高切必须低于奈奎斯特频率")
        if notch_freq is not None and not 0 < float(notch_freq) < self.sfreq / 2:
            raise ValueError("陷波频率必须低于奈奎斯特频率")
        self.notch_freq = None if notch_freq is None else float(notch_freq)
        self.baseline_stabilization = bool(baseline_stabilization)
        self.dc_alpha = float(DISPLAY_FILTER_CONTRACT["baseline_alpha"])
        self.sos_notch: np.ndarray | None = None
        if self.notch_freq is not None:
            self.sos_notch = signal.tf2sos(*signal.iirnotch(self.notch_freq, DISPLAY_FILTER_CONTRACT["notch_q"], self.sfreq))
        self.sos_bp = signal.butter(4, [bp_low, bp_high], btype="bandpass", fs=self.sfreq, output="sos")
        self.dc_offset: np.ndarray | None = None
        self.zi_notch: list[np.ndarray] | None = None
        self.zi_bp: list[np.ndarray] | None = None

    def process(self, samples: np.ndarray) -> np.ndarray:
        """处理 `(samples, channels)` 输入，返回伏特值且支持任意分块。"""
        chunk = np.asarray(samples, dtype=float)
        if chunk.ndim != 2 or chunk.shape[1] != self.channel_count:
            raise ValueError("EEG chunk 必须是 (samples, selected_channels) 矩阵")
        if chunk.shape[0] == 0:
            return chunk.copy()
        extracted = chunk.T
        no_dc = extracted
        if self.baseline_stabilization:
            no_dc = np.empty_like(extracted)
            if self.dc_offset is None:
                self.dc_offset = extracted[:, 0].copy()
            for sample_index in range(extracted.shape[1]):
                self.dc_offset += self.dc_alpha * (extracted[:, sample_index] - self.dc_offset)
                no_dc[:, sample_index] = extracted[:, sample_index] - self.dc_offset
        if self.zi_bp is None:
            if self.sos_notch is not None:
                self.zi_notch = [signal.sosfilt_zi(self.sos_notch) * no_dc[index, 0] for index in range(self.channel_count)]
            self.zi_bp = [signal.sosfilt_zi(self.sos_bp) * no_dc[index, 0] for index in range(self.channel_count)]
        filtered = np.zeros_like(no_dc)
        for index in range(self.channel_count):
            current = no_dc[index]
            if self.sos_notch is not None and self.zi_notch is not None:
                current, self.zi_notch[index] = signal.sosfilt(self.sos_notch, current, zi=self.zi_notch[index])
            current, self.zi_bp[index] = signal.sosfilt(self.sos_bp, current, zi=self.zi_bp[index])
            filtered[index] = current
        return filtered.T

    def snapshot(self) -> FilterState:
        """复制当前状态，供静态窗口检查点缓存。"""
        if self.zi_bp is None:
            raise ValueError("滤波器尚未产生可缓存状态")
        return (self.dc_offset.copy() if self.dc_offset is not None else None, [item.copy() for item in self.zi_notch] if self.zi_notch is not None else None, [item.copy() for item in self.zi_bp])

    def restore(self, state: FilterState) -> None:
        """恢复检查点状态；所有数组均复制，避免并发窗口互相污染。"""
        dc_offset, zi_notch, zi_bp = state
        self.dc_offset = dc_offset.copy() if dc_offset is not None else None
        self.zi_notch = [item.copy() for item in zi_notch] if zi_notch is not None else None
        self.zi_bp = [item.copy() for item in zi_bp] if zi_bp is not None else None
