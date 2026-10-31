"""Serial TNK frame input preparation."""

from __future__ import annotations

import logging
import time
from copy import copy
from dataclasses import is_dataclass, replace
from typing import Any

import numpy as np

from app.scientific.quality import SpectralQualityGateError

from .group_graph import TNK_ROLE_ORDER

_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")


def child_config(child: Any, config: Any, start_s: float, end_s: float,
                 primary_role: str = "Fz") -> Any:
    role_map = {"channel": primary_role, "dependencies": "Fz"}
    source_channels = [config.role_mapping[role_map.get(role, role)]
                       for role in child.manifest.required_channel_roles]
    payload: dict[str, Any] = {
        "channel": source_channels[0], "mode": "static",
        "start_s": start_s, "end_s": end_s,
    }
    fields = ("secondary_channel", "tertiary_channel", "fourth_channel", "fifth_channel")
    payload.update({field: value for field, value in zip(fields, source_channels[1:])})
    payload.update({
        "window_s": config.window_s, "step_s": config.step_s,
        "iapf_hz": config.iapf_hz, "ema_alpha": config.ema_alpha,
        "warmup_count": config.warmup_count, "min_clean_epochs": config.min_clean_epochs,
        "fit_low_hz": config.fit_low_hz, "fit_high_hz": config.fit_high_hz,
    })
    return child.config_model.model_validate(payload)


def spectrum_channel_failure(spectrum: Any, channels: tuple[str, ...]) -> SpectralQualityGateError | None:
    psd = np.asarray(getattr(spectrum, "psd", []), dtype=float)
    if psd.ndim != 2 or psd.shape[0] != len(channels):
        return None
    invalid = [index for index, row in enumerate(psd) if not np.isfinite(row).all()]
    if not invalid:
        return None
    evidence = getattr(spectrum, "evidence", {}) or {}
    channel_quality = evidence.get("channel_quality", {})
    rejected = {
        channels[index]: channel_quality.get(channels[index], {
            "status": "gate_failed", "rejected_reasons": ["non_finite_psd"],
        })
        for index in invalid
    }
    reasons = sorted({
        reason for item in rejected.values()
        for reason in item.get("rejected_reasons", ["non_finite_psd"])
    })
    first = next(iter(rejected.values()))
    quality = dict(first.get("failure", {}))
    quality.update({
        "clean_segments": getattr(spectrum, "clean_epochs", quality.get("clean_segments", 0)),
        "total_segments": getattr(spectrum, "total_epochs", quality.get("total_segments", 0)),
        "clean_ratio": getattr(spectrum, "signal_quality", quality.get("clean_ratio", 0.0)),
        "gate_failed": "channel_quality", "rejected_reasons": reasons,
    })
    quality["evidence"] = {**dict(quality.get("evidence", {})), "channel_quality": rejected}
    return SpectralQualityGateError(quality)


def prewarm_shared_spectra(inputs: Any, config: Any,
                           ranges: set[tuple[float, float]],
                           diagnostics: dict[str, int] | None = None) -> dict[tuple[int, int], Any]:
    """Load each frame's mapped channels once before node execution."""
    shared: dict[tuple[int, int], Any] = {}
    channels = tuple(config.role_mapping[role] for role in TNK_ROLE_ORDER)
    for start_s, end_s in sorted(ranges):
        started = time.perf_counter()
        key = (int(round(start_s * inputs.sfreq_hz)), int(round(end_s * inputs.sfreq_hz)))
        try:
            spectrum = inputs.payload.load_spectrum(
                start_s=start_s, window_s=end_s - start_s, channels=list(channels),
                filter_low_hz=1.0, filter_high_hz=30.0, output_low_hz=1.0,
                output_high_hz=30.0, preprocessing_scope="tnk_window_scoped_v1",
            )
        except SpectralQualityGateError as exc:
            shared[key] = (tuple(channel.casefold() for channel in channels), exc)
            channel_results: list[Any | None] = []
            channel_quality: dict[str, Any] = {}
            reference = None
            for channel in channels:
                channel_key = channel.casefold()
                try:
                    estimate = inputs.payload.load_spectrum(
                        start_s=start_s, window_s=end_s - start_s, channels=[channel],
                        filter_low_hz=1.0, filter_high_hz=30.0, output_low_hz=1.0,
                        output_high_hz=30.0, preprocessing_scope="tnk_window_scoped_v1",
                    )
                except SpectralQualityGateError as channel_error:
                    channel_results.append(None)
                    channel_quality[channel_key] = {
                        "status": "gate_failed", "failure": channel_error.quality,
                        "rejected_reasons": list(channel_error.quality.get("rejected_reasons", [])),
                    }
                    continue
                values = np.asarray(getattr(estimate, "psd", []), dtype=float)
                if values.ndim != 2 or values.shape[0] != 1 or not np.isfinite(values).all():
                    channel_results.append(None)
                    channel_quality[channel_key] = {
                        "status": "gate_failed", "rejected_reasons": ["non_finite_psd"],
                    }
                    continue
                channel_results.append(estimate)
                reference = reference or estimate
                channel_quality[channel_key] = {
                    "status": "clean", "signal_quality": getattr(estimate, "signal_quality", None),
                    "rejected_reasons": list(getattr(estimate, "rejected_reasons", ())),
                }
            if reference is None:
                shared[key] = (tuple(channel.casefold() for channel in channels), exc)
                continue
            rows = [
                np.asarray(estimate.psd[0], dtype=float)
                if estimate is not None else np.full(reference.psd.shape[1], np.nan, dtype=float)
                for estimate in channel_results
            ]
            evidence = dict(getattr(reference, "evidence", {}) or {})
            evidence.update({"channel_quality": channel_quality, "channel_isolated_fallback": True})
            clean_quality = [float(item["signal_quality"]) for item in channel_quality.values()
                             if item.get("status") == "clean" and item.get("signal_quality") is not None]
            if is_dataclass(reference):
                spectrum = replace(reference, psd=np.vstack(rows), gate_failed=None,
                                   signal_quality=min(clean_quality) if clean_quality else 0.0,
                                   evidence=evidence)
            else:
                spectrum = copy(reference)
                spectrum.psd, spectrum.gate_failed, spectrum.evidence = np.vstack(rows), None, evidence
            shared[key] = (tuple(channel.casefold() for channel in channels), spectrum)
            _LOGGER.info("tnk_shared_spectrum_perf samples=%s:%s state=partial_quality rejected_channels=%s elapsed_ms=%.1f",
                         key[0], key[1], [channel for channel, item in channel_quality.items()
                                          if item.get("status") != "clean"],
                         (time.perf_counter() - started) * 1000)
            continue
        shared[key] = (tuple(channel.casefold() for channel in channels), spectrum)
        _LOGGER.info("tnk_shared_spectrum_perf samples=%s:%s state=ready elapsed_ms=%.1f",
                     key[0], key[1], (time.perf_counter() - started) * 1000)
        if diagnostics is not None:
            diagnostics["spectrum_loads"] = diagnostics.get("spectrum_loads", 0) + 1
    return shared


def child_inputs_and_window(inputs: Any, child: Any, child_config: Any,
                            start_s: float, end_s: float,
                            spectrum_cache: dict[tuple[Any, ...], Any],
                            diagnostics: dict[str, int] | None,
                            shared_spectra: dict[tuple[int, int], Any]) -> tuple[Any, Any]:
    child_inputs = child.resolve_inputs(inputs.payload, child_config)
    channels = tuple(channel.casefold() for channel in child.requested_channels(child_config))
    start_sample, end_sample = int(round(start_s * inputs.sfreq_hz)), int(round(end_s * inputs.sfreq_hz))
    range_key = (start_sample, end_sample)
    shared = shared_spectra.get(range_key)
    if shared is not None:
        shared_channels, shared_value = shared
        if isinstance(shared_value, SpectralQualityGateError):
            raise shared_value
        shared_spectrum = shared_value
        indexes = [shared_channels.index(channel) for channel in channels]
        if is_dataclass(shared_spectrum):
            spectrum = replace(shared_spectrum, psd=shared_spectrum.psd[indexes])
        else:
            spectrum = copy(shared_spectrum)
            spectrum.psd = shared_spectrum.psd[indexes]
        if diagnostics is not None:
            diagnostics["spectrum_cache_hits"] = diagnostics.get("spectrum_cache_hits", 0) + 1
    else:
        cache_key = (start_sample, end_sample, channels)
        spectrum = spectrum_cache.get(cache_key)
        if spectrum is None:
            spectrum = child.load_window_spectrum(child_inputs, child_config, start_s, end_s, bounded=True)
            spectrum_cache[cache_key] = spectrum
            if diagnostics is not None:
                diagnostics["spectrum_loads"] = diagnostics.get("spectrum_loads", 0) + 1
        elif diagnostics is not None:
            diagnostics["spectrum_cache_hits"] = diagnostics.get("spectrum_cache_hits", 0) + 1
    quality_failure = spectrum_channel_failure(spectrum, channels)
    if quality_failure is not None:
        raise quality_failure
    return child_inputs, child._window(child_inputs, child_config, start_s, end_s, spectrum=spectrum)
