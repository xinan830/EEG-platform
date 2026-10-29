"""Runtime adapters for ANT-derived raw Fz TNK children."""

from __future__ import annotations

import logging
import time
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (
    AlgorithmExecutionSnapshot, AlgorithmManifest, AlgorithmResult, SignalRequirements,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema

from .legacy_biofeedback import CrimsonEngine, ZenliteEngine
from .runtime import TnkRuntimeConfig
from .contracts import TnkManifest

_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")


class _LegacyDefinition:
    def __init__(self, algorithm_id: str, display_name_zh: str, output_unit: str = "score") -> None:
        self.manifest = TnkManifest(
            algorithm_id=algorithm_id, display_name_zh=display_name_zh,
            scientific_version=f"{algorithm_id}-v1", implementation_identity=f"{algorithm_id}-legacy-port-v1",
            channel_roles=["Fz"], output_unit=output_unit, stateful=True,
            failure_codes=["TNK_WARMUP", "TNK_QUALITY_REJECTED", "TNK_MODEL_UNAVAILABLE", "TNK_LEGACY_INPUT_INVALID"],
        )


class TnkLegacyStreamBundle:
    """Share one raw Fz read and the two required causal preprocessing streams.

    Crimson and Zenlite do not have the same target sampling rate or filter
    contract, so their filtered arrays cannot be merged scientifically. They
    do share the raw Fz chunk and each Zenlite metric shares one Zenlite
    pipeline/model state. The bundle is deep-copied with the TNK replay
    checkpoint, which makes forward playback incremental while preserving
    deterministic backward replay.
    """

    def __init__(self) -> None:
        self._sfreq_hz: float | None = None
        self._processed_end_sample = 0
        self._crimson: CrimsonEngine | None = None
        self._zenlite: ZenliteEngine | None = None

    @property
    def processed_end_sample(self) -> int:
        return self._processed_end_sample

    def reset(self) -> None:
        self._processed_end_sample = 0
        if self._crimson is not None:
            self._crimson.reset()
        if self._zenlite is not None:
            self._zenlite.reset()

    def _ensure_engines(self, sfreq_hz: float) -> None:
        if self._sfreq_hz is not None and self._sfreq_hz != sfreq_hz:
            self._crimson = self._zenlite = None
            self._processed_end_sample = 0
        if self._crimson is None or self._zenlite is None:
            self._crimson = CrimsonEngine(sfreq_hz)
            self._zenlite = ZenliteEngine(sfreq_hz)
            self._sfreq_hz = sfreq_hz

    def advance(self, inputs: Any, *, channel: str, end_sample: int,
                start_s: float, end_s: float) -> dict[str, AlgorithmResult]:
        sfreq_hz = float(inputs.sfreq_hz)
        self._ensure_engines(sfreq_hz)
        source_provenance: dict[str, Any] | None = None
        source_started = time.perf_counter()
        if end_sample < self._processed_end_sample:
            raise ValueError("TNK legacy stream cannot move backward without a checkpoint restore")
        if end_sample == self._processed_end_sample:
            empty = np.empty(0, dtype=np.float64)
        else:
            source = inputs.payload.load_time_series_source(
                start_sample=self._processed_end_sample, end_sample=end_sample,
                channels=[channel], expected_sfreq_hz=sfreq_hz, roles=["Fz"],
            )
            empty = source.values_v[:, 0]
            source_provenance = source.provenance
        source_elapsed_ms = (time.perf_counter() - source_started) * 1000
        assert self._crimson is not None and self._zenlite is not None
        crimson_started = time.perf_counter()
        crimson = self._crimson.process_chunk(empty, start_s=start_s, end_s=end_s)
        crimson_elapsed_ms = (time.perf_counter() - crimson_started) * 1000
        zenlite_started = time.perf_counter()
        zenlite = self._zenlite.process_chunk(empty, start_s=start_s, end_s=end_s)
        zenlite_elapsed_ms = (time.perf_counter() - zenlite_started) * 1000
        self._processed_end_sample = end_sample
        _LOGGER.info(
            "tnk_legacy_stream_perf end_sample=%s source_read_ms=%.1f crimson_ms=%.1f zenlite_group_ms=%.1f",
            end_sample, source_elapsed_ms, crimson_elapsed_ms, zenlite_elapsed_ms,
        )
        results = {
            "tnk_crimson_meditation": crimson,
            "tnk_zenlite_calmness": zenlite["tnk_zenlite_calmness"],
            "tnk_zenlite_awareness": zenlite["tnk_zenlite_awareness"],
            "tnk_zenlite_spectral_relaxation": zenlite["tnk_zenlite_spectral_relaxation"],
        }
        if source_provenance is not None:
            for result in results.values():
                result.evidence["extensions"]["tnk_evidence"]["source_provenance"] = source_provenance
        return results


class TnkLegacyRuntimeAdapter:
    """Load canonical Fz samples and replay the old stream from the run origin.

    Each target is replayed from ``config.start_s`` to ``end_s``.  This keeps
    causal filter/resampler and smoothing state deterministic across full and
    on-demand execution; it is intentionally not presented as live acquisition.
    """

    signal_representation = "time_series"
    config_model = TnkRuntimeConfig
    on_demand_default_enabled = True

    def __init__(self, definition: _LegacyDefinition, kind: str) -> None:
        self.module = definition
        self.manifest = self._build_manifest(definition.manifest)
        self.kind = kind
        self.stream_bundle: TnkLegacyStreamBundle | None = None

    def reset_stream(self) -> None:
        if self.stream_bundle is not None:
            self.stream_bundle.reset()

    def compute_stream_frame(self, inputs: Any, *, start_s: float, end_s: float,
                             end_sample: int) -> AlgorithmResult:
        if self.stream_bundle is None:
            raise ValueError("TNK legacy stream bundle is not configured")
        result = self.stream_bundle.advance(
            inputs, channel=inputs.channel, end_sample=end_sample,
            start_s=start_s, end_s=end_s,
        )[self.manifest.algorithm_id]
        result.channel = inputs.channel
        result.requested_range = {"start_s": start_s, "end_s": end_s}
        result.actual_range = {"start_s": start_s, "end_s": end_s}
        return result

    @staticmethod
    def _build_manifest(source: TnkManifest) -> AlgorithmManifest:
        fields = [{"name": source.algorithm_id, "unit": source.output_unit, "meaning": source.display_name_zh}]
        from app.algorithms.capabilities import declared_output_contract, tnk_dynamic_capabilities
        manifest = AlgorithmManifest(
            algorithm_id=source.algorithm_id, display_name_zh=source.display_name_zh,
            abbreviation=source.algorithm_id.upper(), purpose_zh=source.display_name_zh,
            scientific_version=source.scientific_version, is_current_version=True,
            implementation_identity=source.implementation_identity,
            supported_modes=["dynamic"], output_schema={"fields": fields, "window_shapes": {source.algorithm_id: [1]}},
            signal_requirements=SignalRequirements(
                input_roles=["Fz"], representation="time_series", source_shape="single",
                preprocessing_scope="module_defined", missing_sample_policy="reject",
            ), execution_kind="legacy_tnk_time_series", availability="available", is_runnable=True,
            required_channel_roles=["Fz"], catalog_group="TNK 脑指标",
            dynamic_capabilities=tnk_dynamic_capabilities(),
            definition_persistence={"name": source.display_name_zh, "version": "1.0.0"},
        )
        manifest.output_contract = declared_output_contract(
            manifest, static_result_type="scalar", dynamic_result_type="series", evidence_fields=["tnk_evidence"],
        )
        return manifest

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="Fz 分析通道", value_type="string"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ])

    def requested_channels(self, config: TnkRuntimeConfig) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: TnkRuntimeConfig) -> AlgorithmExecutionSnapshot:
        return AlgorithmExecutionSnapshot(
            window={"start_s": config.start_s, "end_s": config.end_s},
            filters={"source": "recording.load_time_series_source", "target_sfreq_hz": 250 if self.kind == "crimson" else 256},
            quality_rules={"tnk_module": self.manifest.algorithm_id, "input_unit": "V", "channel_role": "Fz"},
        )

    def resolve_inputs(self, recording: Any, config: TnkRuntimeConfig):
        labels = {str(item).casefold(): str(item) for item in getattr(recording, "channel_names", getattr(recording, "channels", []))}
        if config.channel.casefold() not in labels:
            raise ValueError(f"TNK source channel does not exist: {config.channel}")
        from app.algorithm_runtime.contracts import AlgorithmInputs
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=labels[config.channel.casefold()],
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    def compute_window(self, inputs: Any, config: TnkRuntimeConfig, start_s: float, end_s: float) -> AlgorithmResult:
        start_s = float(config.start_s)
        request_start = max(0, int(round(start_s * inputs.sfreq_hz)))
        request_end = min(int(round(end_s * inputs.sfreq_hz)), int(round(inputs.duration_s * inputs.sfreq_hz)))
        try:
            source = inputs.payload.load_time_series_source(
                start_sample=request_start, end_sample=request_end, channels=[inputs.channel],
                expected_sfreq_hz=inputs.sfreq_hz, roles=["Fz"],
            )
            values = source.values_v[:, 0]
            if self.kind == "crimson":
                result = CrimsonEngine(inputs.sfreq_hz).run(values, start_s=start_s, end_s=end_s)
            else:
                results = ZenliteEngine(inputs.sfreq_hz).run(values, start_s=start_s, end_s=end_s)
                result = results[self.manifest.algorithm_id]
        except (AttributeError, KeyError, ValueError) as exc:
            from app.algorithm_runtime.contracts import AlgorithmFailure
            result = AlgorithmResult(
                value=None, unit=self.module.manifest.output_unit, channel=inputs.channel,
                requested_range={"start_s": start_s, "end_s": end_s},
                actual_range={"start_s": start_s, "end_s": end_s}, quality="unavailable",
                failure=AlgorithmFailure(code="TNK_LEGACY_INPUT_INVALID", message=str(exc)), evidence={},
            )
            return result
        result.channel = inputs.channel
        result.evidence["extensions"]["tnk_evidence"]["source_provenance"] = source.provenance
        return result


def build_tnk_legacy_adapters() -> tuple[TnkLegacyRuntimeAdapter, ...]:
    adapters = [
        TnkLegacyRuntimeAdapter(_LegacyDefinition("tnk_crimson_meditation", "TNK 神经网络冥想度"), "crimson"),
        TnkLegacyRuntimeAdapter(_LegacyDefinition("tnk_zenlite_calmness", "TNK 平静度"), "zenlite"),
        TnkLegacyRuntimeAdapter(_LegacyDefinition("tnk_zenlite_awareness", "TNK 觉察度"), "zenlite"),
        TnkLegacyRuntimeAdapter(_LegacyDefinition("tnk_zenlite_spectral_relaxation", "TNK 频谱放松度"), "zenlite"),
    ]
    bundle = TnkLegacyStreamBundle()
    for adapter in adapters:
        adapter.stream_bundle = bundle
    return tuple(adapters)

