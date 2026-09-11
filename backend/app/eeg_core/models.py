from dataclasses import dataclass, field
from typing import List

import numpy as np


@dataclass(frozen=True)
class StreamInfo:
    sfreq: float
    ch_names: List[str]


@dataclass(frozen=True)
class EventMarker:
    time_s: float
    label: str


@dataclass(frozen=True)
class WaveformFrame:
    x_data: np.ndarray
    y_data: np.ndarray
    channel_names: List[str]
    elapsed_seconds: float
    sweep_cycle: int
    event_markers: List[EventMarker] = field(default_factory=list)


@dataclass(frozen=True)
class CalibrationResult:
    label: str
    iapf: float
    calibrated: bool
    signal_quality: float        # 干净信号比例 = clean_duration_s / total_duration_s
    total_duration_s: float
    clean_duration_s: float
    bad_duration_s: float
    duration_s: float
    freqs: np.ndarray
    avg_psd: np.ndarray
    aperiodic_fit: np.ndarray
    model_fit: np.ndarray
    fit_freqs: np.ndarray
    aperiodic_exponent: float
    aperiodic_offset: float
    model_r2: float
    model_error: float
    gaussian_cf: float
    peak_power: float
    peak_bw: float
    cog: float
    gate_failed: str
    fz_psd: np.ndarray = None
    pz_psd: np.ndarray = None
    peak_exists: bool = False
    peak_quality: str = ""
    alpha_residual_ratio: float = float("nan")
    iapf_source: str = ""        # 本窗 IAPF 取值来源：'peak'(高斯峰中心)/'cog'(重心)/''(无结果)
    # 逐通道 PSD（键 = 通道名，与 freqs 等长）。下游 1/f 斜率与残差 RBP 直接复用它，
    # 不再自行滤波/算谱——保证实时趋势与时段报告出自同一份谱。质量门未过时为 None。
    channel_psds: dict = None
