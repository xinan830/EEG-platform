"""SciPy odd-padding forward/backward filtering with block-carried state."""

from __future__ import annotations

from collections.abc import Callable

import numpy as np
from scipy import signal

from app.scientific.contracts.analysis import ANALYSIS_CONTRACT


def zero_phase_blocks(source: np.ndarray, target: np.ndarray, allocate: Callable,
                      *, b=None, a=None, sos=None, block_samples: int = 65536,
                      release: Callable = lambda array: None,
                      check_cancelled: Callable[[], None] = lambda: None) -> None:
    """Match SciPy's default odd extension and steady-state initial conditions.

    Allocation may use disk-backed arrays. State is not reset at block boundaries;
    NaNs propagate just as in the full-array SciPy implementation.
    """
    if sos is not None:
        taps = 2 * len(sos) + 1 - min(int((sos[:, 2] == 0).sum()), int((sos[:, 5] == 0).sum()))
        zi = signal.sosfilt_zi(sos)[:, :, None]
        def apply(values, state):
            return signal.sosfilt(sos, values, axis=0, zi=state)
    else:
        taps = max(len(a), len(b))
        zi = signal.lfilter_zi(b, a)[:, None]
        def apply(values, state):
            return signal.lfilter(b, a, values, axis=0, zi=state)
    edge = 3 * taps
    count, channels = source.shape
    if count <= edge:
        raise ValueError("记录太短，无法完成离线零相位滤波")
    padded = allocate((count + 2 * edge, channels))
    forward = allocate(padded.shape)
    try:
        padded[:edge] = 2 * source[0] - source[1:edge + 1][::-1]
        padded[-edge:] = 2 * source[-1] - source[-edge - 1:-1][::-1]
        for start in range(0, count, block_samples):
            check_cancelled()
            padded[edge + start:edge + min(start + block_samples, count)] = source[start:start + block_samples]
        state = zi * padded[0]
        for start in range(0, len(padded), block_samples):
            check_cancelled()
            stop = min(start + block_samples, len(padded))
            forward[start:stop], state = apply(padded[start:stop], state)
        state = zi * forward[-1]
        for stop in range(len(padded), 0, -block_samples):
            check_cancelled()
            start = max(0, stop - block_samples)
            filtered, state = apply(forward[start:stop][::-1].copy(), state)
            left, right = max(start, edge), min(stop, edge + count)
            if right > left:
                target[left - edge:right - edge] = filtered[::-1][left - start:right - start]
    finally:
        release(padded)
        release(forward)


def preprocess_blocks(source: np.ndarray, target: np.ndarray, sfreq: float,
                      allocate: Callable, *, low_hz: float, high_hz: float,
                      notch_hz: float | None, notch_q: float = 30.0,
                      block_samples: int = 65536,
                      release: Callable = lambda array: None,
                      check_cancelled: Callable[[], None] = lambda: None) -> None:
    if not np.isfinite([sfreq, low_hz, high_hz]).all() or not 0 < low_hz < high_hz < sfreq / 2:
        raise ValueError("预处理频率范围必须位于奈奎斯特频率以内")
    current = source
    if notch_hz is not None:
        if not 0 < notch_hz < sfreq / 2 or not np.isfinite(notch_q) or notch_q <= 0:
            raise ValueError("陷波频率或质量因数无效")
        b, a = signal.iirnotch(notch_hz, notch_q, fs=sfreq)
        current = allocate(source.shape)
        zero_phase_blocks(source, current, allocate, b=b, a=a, block_samples=block_samples,
                          check_cancelled=check_cancelled, release=release)
    sos = signal.butter(int(ANALYSIS_CONTRACT["bandpass_prototype_order"]),
                        [low_hz, high_hz], btype="bandpass", fs=sfreq, output="sos")
    zero_phase_blocks(current, target, allocate, sos=sos, block_samples=block_samples,
                      check_cancelled=check_cancelled, release=release)
    if current is not source:
        release(current)
