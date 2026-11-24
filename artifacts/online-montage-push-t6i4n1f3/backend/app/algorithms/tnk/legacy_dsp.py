"""DSP primitives preserved from ANT-Amplify's Fz biofeedback paths.

These routines intentionally keep the old FFTPACK packing, causal filter
state, quality counters, and float32 smoothing rules.  They are not shared
with the platform PSD calculators because their units and frequency semantics
are different.
"""

from __future__ import annotations

import math

import numpy as np
from scipy.fftpack import rfft


class StreamingResampler:
    """Causal rational FIR resampler with state carried across chunks."""

    def __init__(self, sfreq: float, target: int, *, allow_upsampling: bool = False) -> None:
        from fractions import Fraction
        from scipy.signal import firwin

        sfreq = float(sfreq)
        if not np.isfinite(sfreq) or sfreq <= 0 or (sfreq < target and not allow_upsampling):
            raise ValueError(f"源采样率必须不低于{target}Hz")
        ratio = Fraction(target / sfreq).limit_denominator(10000)
        if not np.isclose(float(ratio), target / sfreq, rtol=1e-9, atol=0):
            raise ValueError("无法精确表示重采样比例")
        self.up, self.down = ratio.numerator, ratio.denominator
        self.taps = np.ones(1) if self.up == self.down else (
            firwin(20 * max(self.up, self.down) + 1,
                   1 / max(self.up, self.down), window=("kaiser", 5.0)) * self.up
        )
        self.delay_s = (len(self.taps) - 1) / (2 * self.up * sfreq)
        self._phases = [self.taps[i::self.up] for i in range(self.up)]
        self._keep = max(map(len, self._phases)) - 1 + (self.down + self.up - 1) // self.up
        self.reset()

    def reset(self) -> None:
        self._history = np.empty(0, dtype=np.float64)
        self._inputs = 0
        self._outputs = 0

    def process(self, samples: np.ndarray) -> np.ndarray:
        samples = np.asarray(samples, dtype=np.float64)
        if samples.ndim != 1 or not np.all(np.isfinite(samples)):
            raise ValueError("重采样输入必须为有限一维采样")
        if not len(samples):
            return np.empty(0, dtype=np.float64)
        start = self._inputs - len(self._history)
        x = np.concatenate((self._history, samples))
        self._inputs += len(samples)
        end = self._inputs * self.up // self.down
        output = np.empty(end - self._outputs, dtype=np.float64)
        for index, sample_index in enumerate(range(self._outputs, end)):
            source_index, phase = divmod(sample_index * self.down, self.up)
            taps = self._phases[phase]
            right = source_index - start + 1
            left = max(0, right - len(taps))
            segment = x[left:right]
            output[index] = np.dot(taps[:len(segment)], segment[::-1])
        self._outputs = end
        self._history = x[-self._keep:].copy() if self._keep else np.empty(0)
        return output


class CascadedFilter:
    """ANT's three fourth-order recursive sections at one fixed rate."""

    def __init__(self, sfreq: int, bands: tuple[tuple[str, float, float], ...]) -> None:
        self._sections = []
        for kind, low, high in bands:
            w = math.pi * (high - low) / sfreq
            a = math.cos(math.pi * (high + low) / sfreq) / math.cos(w)
            b = math.tan(w)
            r = math.sin(math.pi / 4)
            d = 1 + 2 * b * r + b * b
            feedback = (
                4 * a * (1 + b * r) / d,
                2 * (b * b - 2 * a * a - 1) / d,
                4 * a * (1 - b * r) / d,
                -(1 - 2 * b * r + b * b) / d,
            )
            gain = (b * b if kind == "bandpass" else 1) / d
            self._sections.append((kind, feedback, gain, 4 * a, 4 * a * a + 2))
        self.reset()

    def reset(self) -> None:
        self._states = [[0.0] * 4 for _ in self._sections]

    def process(self, samples: np.ndarray) -> np.ndarray:
        output = np.empty(len(samples), dtype=np.float32)
        for index, item in enumerate(np.asarray(samples, dtype=np.float64)):
            value = float(item)
            for state, (kind, feedback, gain, b1, b2) in zip(self._states, self._sections):
                s1, s2, s3, s4 = state
                v = value + ((feedback[1] * s2 + feedback[0] * s1)
                             + feedback[2] * s3 + feedback[3] * s4)
                if kind == "bandpass":
                    value = gain * ((v - 2 * s2) + s4)
                else:
                    value = gain * ((((v - b1 * s1) + b2 * s2) - b1 * s3) + s4)
                state[:] = (v, s1, s2, s3)
            output[index] = value
        return output


class QualityGate:
    def __init__(self, *, low_frequency_count: int) -> None:
        self.low_frequency_count = low_frequency_count
        self.reset()

    def reset(self) -> None:
        self.counter = 0

    @staticmethod
    def amplitude_bad(packet: np.ndarray) -> bool:
        values = np.abs(packet)
        return not len(values) or bool(np.any(values > 200) or np.all(values <= 2))

    def frequency_bad(self, window: np.ndarray) -> bool:
        magnitudes = fft_magnitudes(window)
        if not np.all(np.isfinite(magnitudes)):
            raise ValueError("频谱含非有限数值")
        return bool(np.any(magnitudes[:self.low_frequency_count] > 5))

    def update(self, bad: bool) -> bool:
        self.counter = self.counter + 1 if bad else max(0, self.counter - 1)
        if self.counter >= 16:
            self.counter = 0
            return True
        return False


def fft_magnitudes(window: np.ndarray) -> np.ndarray:
    packed = rfft(np.asarray(window, dtype=np.float32))
    paired = np.abs(packed).astype(np.float64).reshape(-1, 2)
    return np.sqrt(paired[:, 1] ** 2 + paired[:, 0] ** 2).astype(np.float32)


class MovingAverage:
    def __init__(self, size: int) -> None:
        self.size = size
        self.reset()

    def reset(self) -> None:
        self._values = np.zeros(self.size, dtype=np.float32)
        self._write = self._count = 0

    def add(self, value: float) -> float:
        if not np.isfinite(value):
            raise ValueError("分数必须为有限数值")
        self._values[self._write] = np.float32(value)
        self._write = (self._write + 1) % self.size
        self._count = min(self._count + 1, self.size)
        total = np.float32(0)
        for item in self._values[:self._count]:
            total = np.float32(total + item)
        return float(np.float32(total / np.float32(self._count)))


def zenlite_band_amplitudes(magnitudes: np.ndarray) -> dict[str, float]:
    values = np.asarray(magnitudes, dtype=np.float64)
    if values.shape != (600,) or not np.all(np.isfinite(values)):
        raise ValueError("频谱幅值无效")
    frequencies = np.arange(600, dtype=np.float64) * 256 / 1200
    bands = (("delta", .5, 4), ("theta", 4, 8), ("alpha", 8, 12),
             ("beta_low", 12, 22), ("beta_high", 22, 32), ("gamma", 32, 56))
    return {
        name: sum(float(item) / 1200 for item in values[(frequencies > low) & (frequencies <= high)])
        for name, low, high in bands
    }


def zenlite_relaxation_raw(bands: dict[str, float]) -> float | None:
    alpha, beta_low, beta_high = (float(bands[key]) for key in ("alpha", "beta_low", "beta_high"))
    if not all(np.isfinite(value) for value in (alpha, beta_low, beta_high)) or min(alpha, beta_low, beta_high) <= 0:
        return None
    ratio = np.float32(alpha / (beta_low + beta_high))
    mapped = np.float32(np.float32(ratio + np.float32(-.10000000149011612))
                        / np.float32(.7999999523162842)) * np.float32(100)
    return float(np.float32(np.clip(mapped, 0, 100)))

