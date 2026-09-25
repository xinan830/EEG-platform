"""Resolve the closed user-metric feature vocabulary from offline spectral data."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from app.eeg_core.definition_engine import DefinitionEngineError
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.primitives.units import Unit
from app.scientific.quality.spectral import SpectralQualityGateError
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
    spectral_evidence: dict[str, object]


class DefinitionMetricRunner:
    def __init__(self, recordings: Any):
        self.recordings = recordings

    def resolve_inputs(
        self,
        recording: Any,
        draft: DefinitionVersionDraft,
        config: DefinitionMetricConfig,
    ) -> MetricInputResolution:
        return self.resolve_window(
            recording,
            draft,
            config.channel,
            float(config.time.start_s),
            float(config.time.end_s),
        )

    def resolve_window(
        self,
        recording: Any,
        draft: DefinitionVersionDraft,
        channel: str,
        start_s: float,
        end_s: float,
    ) -> MetricInputResolution:
        try:
            payload = self.recordings.load_spectrum(
                recording,
                start_s,
                end_s - start_s,
                [channel],
            )
        except SpectralQualityGateError:
            raise
        except ValueError as exc:
            raise DefinitionEngineError(
                "METRIC_CHANNEL_UNAVAILABLE", "metric channel is unavailable", {"channel": channel}
            ) from exc

        channels = payload.get("channels", [])
        if channel not in channels:
            raise DefinitionEngineError(
                "METRIC_CHANNEL_UNAVAILABLE", "metric channel is unavailable", {"channel": channel}
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
                value = float(payload[source][channel][band])
            except (KeyError, TypeError, ValueError) as exc:
                raise DefinitionEngineError(
                    "METRIC_FEATURE_UNAVAILABLE", "metric feature is unavailable", {"input": input_name, "feature": feature}
                ) from exc
            inputs[input_name] = Scalar(value, unit)
            snapshot[input_name] = {"feature": str(feature), "value": value, "unit": unit.value, "channel": channel}

        start = float(payload.get("window_start_s", start_s))
        duration = float(payload.get("window_duration_s", end_s - start_s))
        spectral_evidence = {
            "sfreq_hz": float(payload["sfreq_hz"]),
            "analysis_reference": payload["analysis_reference"],
            "algorithm_version": payload["algorithm_version"],
            "filter_contract": payload["filter_contract"],
            "welch_contract": payload["welch_contract"],
            "units": payload["units"],
            "frequencies_hz": list(payload["frequencies_hz"]),
            "psd_uV2_per_hz": list(payload["psd"][channel]),
            "band_power": dict(payload["band_power"][channel]),
            "relative_band_power": dict(payload["relative_band_power"][channel]),
            "quality": dict(payload["quality"]),
        }
        return MetricInputResolution(
            inputs=inputs,
            snapshot=snapshot,
            quality=dict(payload.get("quality", {})),
            actual_range={"start_s": start, "end_s": start + duration},
            spectral_evidence=spectral_evidence,
        )
