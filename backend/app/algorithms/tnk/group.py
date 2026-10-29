"""Public TNK group adapter and child-result aggregation."""

from __future__ import annotations

import logging
import time
from copy import copy, deepcopy
from dataclasses import is_dataclass, replace
from typing import Any

import numpy as np
from pydantic import model_validator

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmManifest,
    AlgorithmModule,
    AlgorithmResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    CapabilityEvidenceStatus,
    DynamicAnalysisPolicy,
    DynamicStrategyCapabilities,
    SignalRequirements,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.errors import UnsupportedAlgorithmModeError
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames
from app.scientific.quality import SpectralQualityGateError

from .group_window import TnkGroupWindowCapability

_ROLES = ("Fz", "Pz", "Oz", "F3", "F4")
_LOGGER = logging.getLogger("brain_platform.dynamic_window.tnk")
_CHILD_ORDER = (
    "tnk_iapf", "tnk_relaxation", "tnk_spatial_distribution",
    "tnk_rhythm_stability", "tnk_composite_meditation", "tnk_brainbeat",
    "tnk_aperiodic", "tnk_brainbeat_flat", "tnk_fatigue", "tnk_hai",
    "tnk_faa", "tnk_rbp", "tnk_crimson_meditation", "tnk_zenlite_calmness",
    "tnk_zenlite_awareness", "tnk_zenlite_spectral_relaxation",
)


class TnkGroupConfig(AlgorithmConfigBase):
    """One immutable, explicit source mapping shared by all child metrics."""

    role_mapping: dict[str, str]
    iapf_hz: float = 10.0
    ema_alpha: float = 0.15
    warmup_count: int = 3
    min_clean_epochs: int = 10
    fit_low_hz: float = 3.0
    fit_high_hz: float = 30.0

    @model_validator(mode="after")
    def validate_mapping(self) -> "TnkGroupConfig":
        if set(self.role_mapping) != set(_ROLES):
            raise ValueError("TNK 脑指标组必须显式映射 Fz、Pz、Oz、F3、F4")
        if any(not isinstance(channel, str) or not channel.strip() for channel in self.role_mapping.values()):
            raise ValueError("TNK 脑指标组来源通道不能为空")
        normalized = [channel.strip().casefold() for channel in self.role_mapping.values()]
        if len(set(normalized)) != len(normalized):
            raise ValueError("TNK 脑指标组的五个角色必须映射到不同来源通道")
        return self


class TnkBiofeedbackGroupAdapter(TnkGroupWindowCapability):
    """Coordinate child input preparation; child modules retain all science."""

    config_model = TnkGroupConfig
    on_demand_default_enabled = True
    # v2 separates the IAPF family's 30 s / 5 s schedule from the group's
    # one-second 4 s metric timeline; cached v1 timelines are not equivalent.
    window_policy_version = "tnk-biofeedback-dynamic-window-v3-flat-current-fit"

    def __init__(self, children: tuple[Any, ...]) -> None:
        super().__init__()
        self.children = tuple(sorted(children, key=lambda child: _CHILD_ORDER.index(child.manifest.algorithm_id)))
        self._child_by_id = {child.manifest.algorithm_id: child for child in self.children}
        self._metric_units = self._build_metric_units()
        metric_count = len(self._metric_units)
        fields = [{"name": "metric_index", "unit": "index", "meaning": "TNK 子指标顺序"}]
        fields.extend({"name": name, "unit": unit, "meaning": name} for name, unit in self._metric_units.items())
        self.manifest = AlgorithmManifest(
            algorithm_id="tnk_biofeedback", display_name_zh="TNK 脑指标",
            abbreviation="TNK", purpose_zh="统一来源通道映射的 TNK 脑指标组",
            scientific_version="tnk-biofeedback-group-v2", is_current_version=True,
            implementation_identity="tnk-biofeedback-group-coordinator-v4-legacy-model-handle",
            supported_modes=["dynamic"],
            dynamic_policy=DynamicAnalysisPolicy(
                minimum_window_s=4.0,
                # The group publishes a one-second timeline. Each child gets
                # its own frozen scientific input window in _execute_children.
                window_options_s=[4.0],
                default_window_s=4.0,
                refresh_step_s=1.0,
                refresh_step_options_s=[1.0],
                allow_warmup=True,
            ),
            output_schema={
                "fields": fields,
                "window_shapes": {item["name"]: [metric_count] if item["name"] == "metric_index" else [1] for item in fields},
                "window_output_kind": "algorithm_group",
                "children": self._child_catalog_contract(),
            },
            signal_requirements=SignalRequirements(
                input_roles=list(_ROLES), representation="mixed", source_shape="paired",
                preprocessing_scope="module_defined",
            ),
            execution_kind="official_composite_run_adapter", availability="available",
            is_runnable=True, required_channel_roles=list(_ROLES), catalog_group="TNK 脑指标",
            dynamic_capabilities=DynamicStrategyCapabilities(
                full=CapabilityEvidenceStatus(
                    implementation="implemented", validation="unvalidated",
                    reason="组级 dispatch 与结果契约已有测试，但组级完整数值 parity 尚未验证。",
                    evidence=["backend/tests/test_tnk_group.py"],
                ),
                on_demand=CapabilityEvidenceStatus(
                    implementation="implemented", validation="unvalidated",
                    reason="组级目标窗回放已实现；完整记录上的数值 parity 与人工验收尚待完成。",
                    evidence=["backend/tests/test_tnk_group.py"],
                ),
                combined_preview_background_full=CapabilityEvidenceStatus(implementation="implemented", validation="unvalidated", reason="组级 combined parity 尚待验收。"),
                full_result_to_preview=CapabilityEvidenceStatus(implementation="unsupported", validation="not_applicable", reason="组结果不能从单个标量预览中反向投影。"),
                on_demand_result_to_full=CapabilityEvidenceStatus(implementation="unsupported", validation="not_applicable", reason="组级精确窗口反向复用未声明。"),
            ),
            definition_persistence={"name": "TNK 脑指标", "version": "1.0.0"},
        )
        self.manifest.output_contract = self._output_contract(fields, metric_count)

    def _build_metric_units(self) -> dict[str, str]:
        units: dict[str, str] = {}
        for child in self.children:
            algorithm_id = child.manifest.algorithm_id
            unit = child.module.manifest.output_unit
            if algorithm_id in {"tnk_hai", "tnk_aperiodic", "tnk_fatigue"}:
                roles = child.module.manifest.channel_roles
                units.update({f"{algorithm_id}.{role}": unit for role in roles})
            elif algorithm_id == "tnk_rbp":
                units.update({f"tnk_rbp.{role}.{band}": unit for role in _ROLES for band in child.module.bands})
            else:
                units[algorithm_id] = unit
        return units

    def _child_catalog_contract(self) -> list[dict[str, Any]]:
        """Expose each child's input-role needs without making it selectable."""
        children = []
        for child in self.children:
            algorithm_id = child.manifest.algorithm_id
            roles = (
                list(_ROLES) if algorithm_id == "tnk_rbp"
                else [] if "dependencies" in child.manifest.required_channel_roles
                else list(child.manifest.required_channel_roles)
            )
            children.append({
                "algorithm_id": algorithm_id,
                "display_name_zh": child.manifest.display_name_zh,
                "required_roles": roles,
                "input_source": "child_results" if not roles else "mapped_recording_channels",
                "representation": getattr(child.manifest.signal_requirements, "representation", "spectrum"),
            })
        return children

    @staticmethod
    def _output_contract(fields: list[dict[str, str]], metric_count: int):
        from app.algorithm_runtime.contracts import AlgorithmOutputContract, AlgorithmOutputField

        return AlgorithmOutputContract(
            static_result_type="structured", dynamic_result_type="structured_series",
            channel_role_order=list(_ROLES),
            fields=[AlgorithmOutputField(
                name=item["name"], unit=item["unit"], meaning=item["meaning"],
                shape=[metric_count] if item["name"] == "metric_index" else [1],
            ) for item in fields],
            axes=[{"name": "metric_index", "unit": "index"}],
            quality_values=["clean", "partial", "unavailable", "gate_failed", "insufficient_data", "degraded"],
            dynamic_states=["Partial", "Complete", "Rejected", "Unavailable"],
            failure_semantics="每个子指标分别保留值、单位、质量、失败和 provenance；不可用数值以 NaN 存入受控数组并由子结果标记原因。",
            evidence_fields=["source_quality", "spectral_evidence", "calculation_trace", "tnk_evidence"],
        )

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="string"),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ])

    def requested_channels(self, config: TnkGroupConfig) -> list[str]:
        return [config.role_mapping[role].strip() for role in _ROLES]

    def execution_snapshot(self, config: TnkGroupConfig) -> AlgorithmExecutionSnapshot:
        return AlgorithmExecutionSnapshot(
            window={"start_s": config.start_s, "end_s": config.end_s},
            filters={"source": "recording.load_spectrum_or_time_series"},
            quality_rules={"role_mapping": dict(config.role_mapping), "group_id": self.manifest.algorithm_id},
        )

    def resolve_inputs(self, recording: Any, config: TnkGroupConfig):
        labels = {str(item).casefold(): str(item) for item in getattr(recording, "channel_names", getattr(recording, "channels", []))}
        missing = [value for value in self.requested_channels(config) if value.casefold() not in labels]
        if missing:
            raise ValueError(f"TNK 来源通道在当前记录/导联快照中不存在: {missing}")
        channels = [labels[value.casefold()] for value in self.requested_channels(config)]
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=channels[0],
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    def _child_config(self, child: Any, config: TnkGroupConfig, start_s: float, end_s: float, primary_role: str = "Fz"):
        role_map = {"channel": primary_role, "dependencies": "Fz"}
        source_channels = [config.role_mapping[role_map.get(role, role)] for role in child.manifest.required_channel_roles]
        payload: dict[str, Any] = {"channel": source_channels[0], "mode": "static", "start_s": start_s, "end_s": end_s}
        child_fields = ("secondary_channel", "tertiary_channel", "fourth_channel", "fifth_channel")
        payload.update({field: value for field, value in zip(child_fields, source_channels[1:])})
        payload.update({
            "window_s": config.window_s, "step_s": config.step_s,
            "iapf_hz": config.iapf_hz, "ema_alpha": config.ema_alpha,
            "warmup_count": config.warmup_count, "min_clean_epochs": config.min_clean_epochs,
            "fit_low_hz": config.fit_low_hz, "fit_high_hz": config.fit_high_hz,
        })
        return child.config_model.model_validate(payload)

    def _child_inputs_and_window(
        self, inputs: Any, child: Any, child_config: Any, start_s: float, end_s: float,
        spectrum_cache: dict[tuple[Any, ...], Any], diagnostics: dict[str, int] | None = None,
        shared_spectra: dict[tuple[int, int], Any] | None = None,
    ):
        child_inputs = child.resolve_inputs(inputs.payload, child_config)
        channels = tuple(channel.casefold() for channel in child.requested_channels(child_config))
        start_sample = int(round(start_s * inputs.sfreq_hz))
        end_sample = int(round(end_s * inputs.sfreq_hz))
        range_key = (start_sample, end_sample)
        shared = (shared_spectra or {}).get(range_key)
        if shared is not None:
            shared_channels, shared_value = shared
            if isinstance(shared_value, SpectralQualityGateError):
                raise shared_value
            shared_spectrum = shared_value
            indexes = [shared_channels.index(channel) for channel in channels]
            psd = shared_spectrum.psd[indexes]
            if is_dataclass(shared_spectrum):
                spectrum = replace(shared_spectrum, psd=psd)
            else:
                # Test and legacy recording adapters may return a mutable
                # namespace instead of the scientific dataclass.
                spectrum = copy(shared_spectrum)
                spectrum.psd = psd
            if diagnostics is not None:
                diagnostics["spectrum_cache_hits"] = diagnostics.get("spectrum_cache_hits", 0) + 1
        else:
            cache_key = (start_sample, end_sample, channels)
            spectrum = spectrum_cache.get(cache_key)
            if spectrum is None:
                spectrum = child.load_window_spectrum(child_inputs, child_config, start_s, end_s)
                spectrum_cache[cache_key] = spectrum
                if diagnostics is not None:
                    diagnostics["spectrum_loads"] = diagnostics.get("spectrum_loads", 0) + 1
            elif diagnostics is not None:
                diagnostics["spectrum_cache_hits"] = diagnostics.get("spectrum_cache_hits", 0) + 1
        window = child._window(child_inputs, child_config, start_s, end_s, spectrum=spectrum)
        return child_inputs, window

    def _prewarm_shared_spectra(
        self, inputs: Any, config: TnkGroupConfig,
        ranges: set[tuple[float, float]], diagnostics: dict[str, int] | None = None,
    ) -> dict[tuple[int, int], Any]:
        """Load one mapped-channel spectrum per time range for all children.

        TNK children use different role subsets, but they share the same
        1-30 Hz source contract. Loading the five mapped channels once avoids
        evicting separate one-, two-, and five-channel preprocessing entries
        from the small process cache on every child calculation.
        """
        shared: dict[tuple[int, int], Any] = {}
        channels = tuple(config.role_mapping[role] for role in _ROLES)
        for start_s, end_s in sorted(ranges):
            started = time.perf_counter()
            key = (
                int(round(start_s * inputs.sfreq_hz)),
                int(round(end_s * inputs.sfreq_hz)),
            )
            try:
                spectrum = inputs.payload.load_spectrum(
                    start_s=start_s, window_s=end_s - start_s,
                    channels=list(channels), filter_low_hz=1.0,
                    filter_high_hz=30.0, output_low_hz=1.0,
                    output_high_hz=30.0,
                )
            except SpectralQualityGateError as exc:
                shared[key] = (tuple(channel.casefold() for channel in channels), exc)
                _LOGGER.info(
                    "tnk_shared_spectrum_perf samples=%s:%s state=quality_rejected elapsed_ms=%.1f",
                    key[0], key[1], (time.perf_counter() - started) * 1000,
                )
                continue
            shared[key] = (tuple(channel.casefold() for channel in channels), spectrum)
            _LOGGER.info(
                "tnk_shared_spectrum_perf samples=%s:%s state=ready elapsed_ms=%.1f",
                key[0], key[1], (time.perf_counter() - started) * 1000,
            )
            if diagnostics is not None:
                diagnostics["spectrum_loads"] = diagnostics.get("spectrum_loads", 0) + 1
        return shared

    def _fresh_children(self) -> tuple[Any, ...]:
        """Give each execution isolated state so concurrent strategies cannot cross-contaminate."""
        # Deep-copy the tuple as one graph. The migrated Crimson/Zenlite
        # adapters point at one shared legacy stream bundle; copying children
        # one by one would silently duplicate that state.
        return deepcopy(self.children)

    @staticmethod
    def _reset_legacy_streams(children: tuple[Any, ...]) -> None:
        seen: set[int] = set()
        for child in children:
            bundle = getattr(child, "stream_bundle", None)
            if bundle is None or id(bundle) in seen:
                continue
            seen.add(id(bundle))
            bundle.reset()

    def _execute_children(
        self, inputs: Any, config: TnkGroupConfig, start_s: float, end_s: float,
        *, reset_state: bool, children: tuple[Any, ...] | None = None,
        diagnostics: dict[str, int] | None = None, dynamic_frame: bool = False,
    ):
        active_children = children or self.children
        results: dict[str, list[Any]] = {}
        first_result: dict[str, Any] = {}
        if reset_state:
            for child in active_children:
                if hasattr(child.module, "reset"):
                    child.module.reset()
            self._reset_legacy_streams(active_children)
        spectrum_cache: dict[tuple[Any, ...], Any] = {}
        shared_spectra: dict[tuple[int, int], Any] = {}
        if dynamic_frame:
            ranges: set[tuple[float, float]] = set()
            for child in active_children:
                if getattr(child, "signal_representation", "spectrum") != "spectrum":
                    continue
                child_range = self._dynamic_child_range(child.manifest.algorithm_id, config, start_s, end_s)
                if child_range is not None:
                    ranges.add(child_range)
            shared_spectra = self._prewarm_shared_spectra(inputs, config, ranges, diagnostics)
        legacy_outputs: dict[str, Any] = {}
        legacy_children = [child for child in active_children if getattr(child, "stream_bundle", None) is not None]
        if dynamic_frame and legacy_children and hasattr(inputs.payload, "load_time_series_source"):
            due = [child for child in legacy_children
                   if self._dynamic_child_range(child.manifest.algorithm_id, config, start_s, end_s) is not None]
            if due:
                bundles = {id(getattr(child, "stream_bundle")): getattr(child, "stream_bundle") for child in due}
                if len(bundles) != 1:
                    raise ValueError("TNK legacy children must use one shared stream bundle")
                bundle = next(iter(bundles.values()))
                started = time.perf_counter()
                try:
                    legacy_outputs = bundle.advance(
                        inputs, channel=inputs.channel,
                        end_sample=int(round(end_s * inputs.sfreq_hz)),
                        start_s=start_s, end_s=end_s,
                    )
                    if diagnostics is not None:
                        diagnostics["shared_time_series_loads"] = diagnostics.get("shared_time_series_loads", 0) + 1
                except (AttributeError, KeyError, ValueError):
                    # Preserve the existing structured child-level failure
                    # contract for malformed or unavailable historical input.
                    legacy_outputs = {}
                _LOGGER.info(
                    "tnk_legacy_stream_prepare_perf end_sample=%s output_count=%s elapsed_ms=%.1f",
                    int(round(end_s * inputs.sfreq_hz)), len(legacy_outputs),
                    (time.perf_counter() - started) * 1000,
                )
        for child in active_children:
            child_id = child.manifest.algorithm_id
            child_range = self._dynamic_child_range(child_id, config, start_s, end_s) if dynamic_frame else (start_s, end_s)
            if child_range is None:
                elapsed = end_s - config.start_s
                if child_id == "tnk_faa":
                    code, message = "TNK_SEGMENT_REQUIRED", "TNK FAA 需要完整配对事件段；当前游标动态分析没有事件段输入。"
                elif child_id in {"tnk_iapf", "tnk_hai", "tnk_aperiodic"} and elapsed < 30.0 - 1e-9:
                    code, message = "TNK_CONTEXT_WARMUP", "该指标需要最近 30 秒的连续数据；当前记录位置尚未满足预热长度。"
                else:
                    code, message = "TNK_UPDATE_NOT_DUE", "该指标按冻结策略每 5 秒更新一次；当前游标尚未到更新节拍。"
                results[child_id] = [self._scheduled_child_result(child, start_s, end_s, code, message)]
                first_result[child_id] = results[child_id][0]
                continue
            child_start_s, child_end_s = child_range
            roles = _ROLES if child_id == "tnk_rbp" else ("Fz",)
            child_rows = []
            input_elapsed_ms = 0.0
            compute_elapsed_ms = 0.0
            for role in roles:
                child_config = self._child_config(child, config, child_start_s, child_end_s, primary_role=role)
                if getattr(child, "signal_representation", "spectrum") == "time_series":
                    result = legacy_outputs.get(child_id)
                    if result is None:
                        result = child.compute_window(inputs, child_config, child_start_s, child_end_s)
                    result.channel = "/".join(child.requested_channels(child_config))
                    result.requested_range = {"start_s": child_start_s, "end_s": child_end_s}
                    result.actual_range = {"start_s": child_start_s, "end_s": child_end_s}
                    child_rows.append(result)
                    if diagnostics is not None:
                        diagnostics["time_series_loads"] = diagnostics.get("time_series_loads", 0) + 1
                    continue
                try:
                    input_started = time.perf_counter()
                    child_inputs, window = self._child_inputs_and_window(
                    inputs, child, child_config, child_start_s, child_end_s, spectrum_cache, diagnostics,
                    shared_spectra,
                    )
                    input_elapsed_ms += (time.perf_counter() - input_started) * 1000
                except SpectralQualityGateError as exc:
                    child_rows.append(self._quality_rejected_result(
                        child, child_config, child_start_s, child_end_s, exc,
                    ))
                    continue
                if child_id == "tnk_composite_meditation":
                    spatial = first_result.get("tnk_spatial_distribution")
                    stability = first_result.get("tnk_rhythm_stability")
                    window.provenance.update({
                        "spatial_distribution": spatial.value if spatial else None,
                        "rhythm_stability": stability.value if stability else None,
                        "spatial_valid": bool(spatial and spatial.quality == "clean"),
                        "stability_ready": bool(stability and stability.quality == "clean"),
                    })
                if child_id == "tnk_brainbeat_flat":
                    aperiodic = first_result.get("tnk_aperiodic")
                    if aperiodic:
                        fit_evidence = aperiodic.evidence.get("extensions", {}).get("tnk_evidence", {})
                        exponents = aperiodic.output_values or {}
                        offsets = fit_evidence.get("offsets", {})
                        fit_quality = fit_evidence.get("fit_quality", {})
                        window.provenance["aperiodic_fit_by_role"] = {
                            role: {
                                "offset": offsets[role], "exponent": exponent,
                                "fit_start_s": aperiodic.actual_range["start_s"],
                                "fit_end_s": aperiodic.actual_range["end_s"],
                                "fit_quality": fit_quality.get(role),
                            }
                            for role, exponent in exponents.items()
                            if exponent is not None and role in offsets
                        }
                compute_started = time.perf_counter()
                result = child.module.compute(window, child._tnk_config(child_config, child_inputs.sfreq_hz))
                compute_elapsed_ms += (time.perf_counter() - compute_started) * 1000
                if diagnostics is not None:
                    diagnostics["child_calculations"] = diagnostics.get("child_calculations", 0) + 1
                result.channel = "/".join(child.requested_channels(child_config))
                result.requested_range = {"start_s": child_start_s, "end_s": child_end_s}
                result.actual_range = {"start_s": child_start_s, "end_s": child_end_s}
                child_rows.append(result)
            results[child_id] = child_rows
            if child_rows and getattr(child, "signal_representation", "spectrum") != "time_series":
                _LOGGER.info(
                    "tnk_child_window_perf child_id=%s roles=%s input_ms=%.1f compute_ms=%.1f",
                    child_id, len(child_rows), input_elapsed_ms, compute_elapsed_ms,
                )
            if child_rows:
                first_result[child_id] = child_rows[0]
        return results

    @staticmethod
    def _dynamic_child_range(
        child_id: str, config: TnkGroupConfig, start_s: float, end_s: float,
    ) -> tuple[float, float] | None:
        """Apply each child's frozen rolling window and refresh cadence."""
        if child_id == "tnk_faa":
            return None
        if child_id in {"tnk_iapf", "tnk_hai", "tnk_aperiodic"}:
            duration_s, cadence_s = 30.0, 5.0
            elapsed = end_s - config.start_s
            if elapsed + 1e-9 < duration_s:
                return None
            cadence_index = round(elapsed / cadence_s)
            if not np.isclose(elapsed, cadence_index * cadence_s, rtol=0, atol=1e-7):
                return None
        else:
            duration_s = {
                "tnk_crimson_meditation": 5.0,
                "tnk_zenlite_calmness": 4.6875,
                "tnk_zenlite_awareness": 4.6875,
                "tnk_zenlite_spectral_relaxation": 4.6875,
            }.get(child_id, 4.0)
            if end_s - config.start_s + 1e-9 < duration_s:
                return None
        child_end_s = end_s
        child_start_s = child_end_s - duration_s
        return (child_start_s, child_end_s) if child_start_s >= config.start_s - 1e-9 else None

    @staticmethod
    def _scheduled_child_result(
        child: Any, start_s: float, end_s: float, code: str, message: str,
    ) -> AlgorithmResult:
        roles = list(child.module.manifest.channel_roles)
        channel = "/".join(roles)
        return AlgorithmResult(
            value=None, unit=child.module.manifest.output_unit, channel=channel,
            requested_range={"start_s": start_s, "end_s": end_s},
            actual_range={"start_s": start_s, "end_s": end_s},
            quality="insufficient_data",
            failure=AlgorithmFailure(code=code, message=message),
            output_values={role: None for role in roles} if child.manifest.algorithm_id in {
                "tnk_hai", "tnk_aperiodic", "tnk_fatigue",
            } else None,
            evidence={"extensions": {"tnk_evidence": {
                "provenance": {"algorithm_id": child.manifest.algorithm_id,
                               "schedule_state": "not_due" if code == "TNK_UPDATE_NOT_DUE" else "unsupported"}
            }}},
        )

    @staticmethod
    def _quality_rejected_result(
        child: Any, child_config: Any, start_s: float, end_s: float,
        error: SpectralQualityGateError,
    ) -> AlgorithmResult:
        """Keep a rejected source window local to its child instead of aborting the Run."""
        quality = dict(error.quality)
        channels = child.requested_channels(child_config)
        roles = list(child.module.manifest.channel_roles)
        output_values = {role: None for role in roles} if child.manifest.algorithm_id in {
            "tnk_hai", "tnk_aperiodic", "tnk_fatigue",
        } else None
        return AlgorithmResult(
            value=None,
            unit=child.module.manifest.output_unit,
            channel="/".join(channels),
            requested_range={"start_s": start_s, "end_s": end_s},
            actual_range={"start_s": start_s, "end_s": end_s},
            quality="gate_failed",
            failure=AlgorithmFailure(
                code="TNK_QUALITY_REJECTED",
                message="TNK 窗口未通过共享频谱质量门",
                detail={"quality": quality},
            ),
            output_values=output_values,
            evidence={
                "source_quality": quality,
                "extensions": {"tnk_evidence": {
                    "provenance": {"algorithm_id": child.manifest.algorithm_id},
                    "quality_gate": quality,
                }},
            },
        )

    def _metric_values(self, results: dict[str, list[Any]]) -> tuple[dict[str, float | None], dict[str, Any]]:
        values: dict[str, float | None] = {}
        evidence: dict[str, Any] = {}
        for child_id, rows in results.items():
            evidence[child_id] = [row.model_dump(mode="json") for row in rows]
            if child_id in {"tnk_hai", "tnk_aperiodic", "tnk_fatigue"}:
                for row in rows:
                    for role, value in (row.output_values or {}).items():
                        values[f"{child_id}.{role}"] = value
            elif child_id == "tnk_rbp":
                for role, row in zip(_ROLES, rows, strict=True):
                    for band, value in (row.output_values or {}).items():
                        values[f"tnk_rbp.{role}.{band}"] = value
            else:
                values[child_id] = rows[0].value
        ordered = {name: values.get(name) for name in self._metric_units}
        return ordered, evidence

    @staticmethod
    def _quality(results: dict[str, list[Any]]) -> str:
        qualities = [row.quality for rows in results.values() for row in rows]
        if qualities and all(value == "clean" for value in qualities):
            return "clean"
        if "gate_failed" in qualities:
            return "partial"
        if qualities and all(value in {"gate_failed", "unavailable", "insufficient_data"} for value in qualities):
            return "unavailable"
        return "partial"

    @staticmethod
    def _analysis_state(quality: str) -> str:
        if quality == "clean":
            return "Complete"
        if quality == "unavailable":
            return "Rejected"
        return "Partial"

    @staticmethod
    def _group_failure(quality: str) -> AlgorithmFailure | None:
        if quality != "unavailable":
            return None
        return AlgorithmFailure(
            code="TNK_GROUP_WINDOW_QUALITY_REJECTED",
            message="该动态窗口的全部 TNK 子指标均未通过输入质量门",
        )

    def _point_evidence(self, config: TnkGroupConfig, results: dict[str, list[Any]], values: dict[str, float | None]) -> dict[str, Any]:
        return {"extensions": {"tnk_evidence": {
            "group_id": self.manifest.algorithm_id,
            "role_mapping": dict(config.role_mapping),
            "children": results,
            "metric_values": values,
        }}}

    def execute_static(self, inputs: Any, config: TnkGroupConfig) -> AlgorithmStructuredResult:
        raise UnsupportedAlgorithmModeError(
            "TNK 脑指标组仅支持动态分析。",
            detail={"algorithm_id": self.manifest.algorithm_id, "mode": config.mode,
                    "supported_modes": self.manifest.supported_modes},
        )

    def execute_dynamic(self, inputs: Any, config: TnkGroupConfig) -> AlgorithmStructuredSeriesResult:
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=4.0, step_s=1.0,
            # Every child consumes Welch spectra with a frozen 4 s minimum.
            # Shorter warm-up frames reach the spectral service and fail before
            # the child can return its declared insufficient-data result.
            minimum_window_s=4.0, allow_warmup=True,
        )
        if not frames:
            raise ValueError("TNK 脑指标组没有可执行动态窗口")
        children = self._fresh_children()
        for child in children:
            if hasattr(child.module, "reset"):
                child.module.reset()
        self._reset_legacy_streams(children)
        values_by_metric = {name: [] for name in self._metric_units}
        windows: list[StructuredSeriesWindow] = []
        points = []
        for frame in controlled_frames(frames, inputs.execution_context):
            results = self._execute_children(inputs, config, frame.window_start_s, frame.window_end_s,
                                             reset_state=False, children=children, dynamic_frame=True)
            values, evidence = self._metric_values(results)
            for name, value in values.items():
                values_by_metric[name].append([np.nan if value is None else value])
            quality = self._quality(results)
            point = self._point_evidence(config, evidence, values)
            windows.append(StructuredSeriesWindow(
                start_sample=int(round(frame.window_start_s * inputs.sfreq_hz)),
                end_sample=int(round(frame.window_end_s * inputs.sfreq_hz)),
                start_s=frame.window_start_s, end_s=frame.window_end_s,
                state=self._analysis_state(quality), quality=quality,
                failure=self._group_failure(quality), evidence=point,
            ))
            points.append(point)
        arrays = {name: np.asarray(rows, dtype=float) for name, rows in values_by_metric.items()}
        return AlgorithmStructuredSeriesResult(
            output_kind="algorithm_group", channel_order=self.requested_channels(config),
            axes={"metric_index": np.arange(len(self._metric_units), dtype=float)},
            axis_units={"metric_index": "index"}, arrays=arrays,
            array_units=dict(self._metric_units), windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": config.start_s, "end_s": config.end_s},
            quality="clean" if all(window.quality == "clean" for window in windows) else "partial",
            evidence={"extensions": {"tnk_evidence": {
                "group_id": self.manifest.algorithm_id,
                "role_mapping": dict(config.role_mapping), "points": points,
            }}},
        )
