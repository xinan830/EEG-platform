"""Resolve the closed user-metric feature vocabulary from offline spectral data."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from app.eeg_core.definition_engine import DefinitionEngineError
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.primitives.units import Unit
from app.eeg_core.quality import SpectralQualityGateError
from app.models.algorithm_definition import DefinitionVersionDraft
from app.models.definition_metric_run import DefinitionMetricConfig


_FEATURES: dict[str, tuple[str, str, Unit]] = {
    "delta_power": ("band_power", "delta", Unit.UV2),
    "theta_power": ("band_power", "theta", Unit.UV2),
    "alpha_power": ("band_power", "alpha", Unit.UV2),
    "beta_power": ("band_power", "beta", Unit.UV2),
    "delta_rbp": ("relative_band_power", "delta", Unit.RATIO),
    "theta_rbp": ("relative_band_power", "theta", Unit.RATIO),
    "alpha_rbp": ("relative_band_power", "alpha", Unit.RATIO),
    "beta_rbp": ("relative_band_power", "beta", Unit.RATIO),
}


@dataclass(frozen=True)
class MetricInputResolution:
    inputs: dict[str, Scalar]
    snapshot: dict[str, dict[str, object]]
    quality: dict[str, object]
    actual_range: dict[str, float]


class DefinitionMetricRunner:
    def __init__(self, recordings: Any):
        self.recordings = recordings

    def resolve_inputs(
        self,
        recording: Any,
        draft: DefinitionVersionDraft,
        config: DefinitionMetricConfig,
    ) -> MetricInputResolution:
        try:
            payload = self.recordings.load_spectrum(
                recording,
                float(config.time.start_s),
                float(config.time.end_s - config.time.start_s),
                [config.channel],
            )
        except SpectralQualityGateError:
            raise
        except ValueError as exc:
            raise DefinitionEngineError(
                "METRIC_CHANNEL_UNAVAILABLE", "metric channel is unavailable", {"channel": config.channel}
            ) from exc

        channels = payload.get("channels", [])
        if config.channel not in channels:
            raise DefinitionEngineError(
                "METRIC_CHANNEL_UNAVAILABLE", "metric channel is unavailable", {"channel": config.channel}
            )

        inputs: dict[str, Scalar] = {}
        snapshot: dict[str, dict[str, object]] = {}
        for input_name, metadata in draft.inputs.items():
            feature = metadata.get("feature") if isinstance(metadata, dict) else None
            resolved = _FEATURES.get(str(feature))
            if resolved is None:
                raise DefinitionEngineError(
                    "METRIC_FEATURE_UNSUPPORTED", "unsupported metric feature", {"input": input_name, "feature": feature}
                )
            source, band, unit = resolved
            try:
                value = float(payload[source][config.channel][band])
            except (KeyError, TypeError, ValueError) as exc:
                raise DefinitionEngineError(
                    "METRIC_FEATURE_UNAVAILABLE", "metric feature is unavailable", {"input": input_name, "feature": feature}
                ) from exc
            inputs[input_name] = Scalar(value, unit)
            snapshot[input_name] = {"feature": str(feature), "value": value, "unit": unit.value, "channel": config.channel}

        start = float(payload.get("window_start_s", config.time.start_s))
        duration = float(payload.get("window_duration_s", config.time.end_s - config.time.start_s))
        return MetricInputResolution(
            inputs=inputs,
            snapshot=snapshot,
            quality=dict(payload.get("quality", {})),
            actual_range={"start_s": start, "end_s": start + duration},
        )
