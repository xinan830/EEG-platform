"""Deterministic reference state transitions used by TNK replay tests."""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np


@dataclass
class LogEmaState:
    warmup_count: int = 3
    alpha: float = 0.15
    values: list[float] = field(default_factory=list)
    log_ema: float | None = None

    def update(self, ratio: float | None) -> float | None:
        if ratio is None or not np.isfinite(ratio) or ratio <= 0:
            return None
        logged = float(np.log(ratio))
        if self.log_ema is None:
            self.values.append(logged)
            if len(self.values) < self.warmup_count:
                return None
            self.log_ema = float(np.mean(self.values))
        else:
            self.log_ema = self.alpha * logged + (1.0 - self.alpha) * self.log_ema
        return float(np.exp(self.log_ema))


@dataclass
class StabilityHistory:
    capacity: int = 10
    minimum_ready: int = 8
    values: dict[str, list[float]] = field(default_factory=dict)

    def update(self, channel: str, ratio: float | None) -> float | None:
        if ratio is None or not np.isfinite(ratio):
            return None
        history = self.values.setdefault(channel, [])
        history.append(float(ratio))
        del history[:-self.capacity]
        if len(history) < self.minimum_ready:
            return 0.65
        mean = float(np.mean(history))
        cv = float(np.std(history) / mean) if mean > 0 else 1.0
        return 1.0 / (1.0 + cv)


@dataclass
class IapfLockState:
    required_candidates: int = 3
    candidates: list[float] = field(default_factory=list)
    global_hz: float | None = None

    def update(self, candidate_hz: float | None) -> float | None:
        if self.global_hz is not None:
            return self.global_hz
        if candidate_hz is None or not np.isfinite(candidate_hz):
            return None
        self.candidates.append(float(candidate_hz))
        if len(self.candidates) >= self.required_candidates:
            self.global_hz = float(np.median(self.candidates))
        return self.global_hz

