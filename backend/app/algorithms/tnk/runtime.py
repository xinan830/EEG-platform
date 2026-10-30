"""Runtime/catalog adapter for the independently implemented TNK modules.

The adapter owns only platform plumbing: role-to-channel resolution, PSD
window loading, and conversion to the common runtime result contract. TNK
modules retain formulas, quality gates, and state transitions.
"""

from __future__ import annotations

from types import SimpleNamespace
from typing import Any

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmManifest,
    AlgorithmModule,
    AlgorithmResult,
    AlgorithmSeriesResult,
    SignalRequirements,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithm_runtime.errors import UnsupportedAlgorithmModeError
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames

from .contracts import TnkConfig, TnkSpectralWindow


class TnkRuntimeConfig(AlgorithmConfigBase):
    """Common runtime fields plus optional role channels for TNK modules."""

    secondary_channel: str | None = None
    tertiary_channel: str | None = None
    fourth_channel: str | None = None
    fifth_channel: str | None = None
    iapf_hz: float = 10.0
    ema_alpha: float = 0.15
    warmup_count: int = 3
    min_clean_epochs: int = 10
    fit_low_hz: float = 3.0
    fit_high_hz: float = 30.0


def _failure(code: str, message: str) -> AlgorithmFailure:
    return AlgorithmFailure(code=code, message=message)


class TnkRuntimeAdapter:
    """Adapt one TNK window module to the platform AlgorithmModule protocol."""

    config_model = TnkRuntimeConfig
    on_demand_default_enabled = False

    def __init__(self, module: Any, manifest: AlgorithmManifest) -> None:
        self.module = module
        self.manifest = manifest

    def parameter_schema(self) -> ParameterSchema:
        parameters = [
            AlgorithmParameter(
                key="channel",
                label_zh=(f"{self.manifest.required_channel_roles[0]} 分析通道"
                          if self.manifest.required_channel_roles[0] != "dependencies" else "主分析通道"),
                value_type="string",
            ),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
        ]
        parameters[1] = parameters[1].model_copy(update={
            "options": [ParameterOption(value=mode, label_zh=mode) for mode in self.manifest.supported_modes],
        })
        for index, role in enumerate(self.manifest.required_channel_roles[1:], start=2):
            key = ("secondary_channel", "tertiary_channel", "fourth_channel", "fifth_channel")[index - 2]
            parameters.append(AlgorithmParameter(key=key, label_zh=f"{role} 分析通道", value_type="string"))
        return ParameterSchema(parameters=parameters)

    def requested_channels(self, config: TnkRuntimeConfig) -> list[str]:
        values = [config.channel, config.secondary_channel, config.tertiary_channel,
                  config.fourth_channel, config.fifth_channel]
        return [value for value in values if value]

    def execution_snapshot(self, config: TnkRuntimeConfig) -> AlgorithmExecutionSnapshot:
        return AlgorithmExecutionSnapshot(
            window={"start_s": config.start_s, "end_s": config.end_s},
            filters={"source": "recording.load_spectrum"},
            quality_rules={"tnk_module": self.manifest.algorithm_id},
        )

    def resolve_inputs(self, recording: Any, config: TnkRuntimeConfig) -> AlgorithmInputs:
        labels = {str(item).casefold(): str(item) for item in getattr(recording, "channel_names", getattr(recording, "channels", []))}
        channels = self.requested_channels(config)
        missing = [channel for channel in channels if channel.casefold() not in labels]
        if missing:
            raise ValueError(f"TNK source channels do not exist: {missing}")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=labels[config.channel.casefold()],
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    def load_window_spectrum(
        self, inputs: AlgorithmInputs, config: TnkRuntimeConfig, start_s: float, end_s: float,
        *, bounded: bool = False,
    ) -> Any:
        channels = self.requested_channels(config)
        return inputs.payload.load_spectrum(
            start_s=start_s, window_s=end_s - start_s, channels=channels,
            filter_low_hz=1.0, filter_high_hz=30.0, output_low_hz=1.0, output_high_hz=30.0,
            preprocessing_scope=("tnk_window_scoped_v1" if bounded else "configured_continuous_before_slice"),
        )

    def _window(
        self, inputs: AlgorithmInputs, config: TnkRuntimeConfig, start_s: float, end_s: float,
        *, spectrum: Any | None = None, bounded: bool = False,
    ) -> TnkSpectralWindow:
        channels = self.requested_channels(config)
        if spectrum is None:
            spectrum = self.load_window_spectrum(inputs, config, start_s, end_s, bounded=bounded)
        roles = self.manifest.required_channel_roles
        return TnkSpectralWindow(
            frequencies_hz=spectrum.freqs,
            psd_by_role={role: spectrum.psd[index] for index, role in enumerate(roles)},
            sfreq_hz=inputs.sfreq_hz,
            start_sample=int(round(start_s * inputs.sfreq_hz)),
            end_sample=int(round(end_s * inputs.sfreq_hz)),
            quality="gate_failed" if spectrum.gate_failed else "clean",
            source_quality={"clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed},
            provenance={"algorithm_id": self.manifest.algorithm_id},
        )

    def _tnk_config(self, config: TnkRuntimeConfig, sfreq_hz: float) -> Any:
        # TNK modules consume this small typed-shaped object; the runtime
        # config remains the public parameter contract and carries the same
        # state settings without duplicating scientific formulas.
        return SimpleNamespace(
            start_sample=int(round(config.start_s * sfreq_hz)),
            end_sample=int(round(config.end_s * sfreq_hz)),
            sfreq_hz=sfreq_hz,
            mode=config.mode,
            iapf_hz=config.iapf_hz,
            ema_alpha=config.ema_alpha,
            warmup_count=config.warmup_count,
            min_clean_epochs=config.min_clean_epochs,
            fit_low_hz=config.fit_low_hz,
            fit_high_hz=config.fit_high_hz,
        )

    def _compute(self, inputs: AlgorithmInputs, config: TnkRuntimeConfig, start_s: float, end_s: float, *, bounded: bool = False) -> AlgorithmResult:
        window = self._window(inputs, config, start_s, end_s, bounded=bounded)
        result = self.module.compute(window, self._tnk_config(config, inputs.sfreq_hz))
        # The module consumes stable scientific roles (for example Pz/Oz),
        # while the platform contract identifies the actual source channels
        # selected by the user. Keep those namespaces separate at the adapter
        # boundary so role semantics are preserved without failing validation.
        result.channel = "/".join(self.requested_channels(config))
        result.requested_range = {"start_s": start_s, "end_s": end_s}
        result.actual_range = {"start_s": start_s, "end_s": end_s}
        return result

    def execute_static(self, inputs: AlgorithmInputs, config: TnkRuntimeConfig) -> AlgorithmResult:
        if hasattr(self.module, "reset"):
            self.module.reset()
        return self._compute(inputs, config, config.start_s, config.end_s)

    def execute_dynamic(self, inputs: AlgorithmInputs, config: TnkRuntimeConfig) -> AlgorithmSeriesResult:
        if self.manifest.algorithm_id == "tnk_faa":
            raise UnsupportedAlgorithmModeError(
                "TNK FAA 需要完整 paired segment，不支持任意游标动态分析",
                detail={"algorithm_id": self.manifest.algorithm_id, "mode": "dynamic", "reason_code": "TNK_FAA_SEGMENT_SCOPE_REQUIRED"},
            )
        if hasattr(self.module, "reset"):
            self.module.reset()
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("TNK 动态分析区间没有可执行窗口")
        values: list[float | None] = []
        output_values: list[dict[str, float | None]] = []
        qualities: list[str] = []
        failures: list[AlgorithmFailure | None] = []
        point_evidence: list[dict[str, Any]] = []
        windows: list[dict[str, float]] = []
        for frame in controlled_frames(frames, inputs.execution_context):
            item = self._compute(inputs, config, frame.window_start_s, frame.window_end_s, bounded=True)
            values.append(item.value)
            output_values.append(item.output_values or {})
            qualities.append(item.quality)
            failures.append(item.failure)
            point_evidence.append(item.evidence)
            windows.append({"start_s": frame.window_start_s, "end_s": frame.window_end_s})
        states = [
            "Rejected" if failure is not None and quality in {"unavailable", "gate_failed"}
            else "Partial" if quality in {"partial", "insufficient_data"}
            else "Complete"
            for failure, quality in zip(failures, qualities)
        ]
        return AlgorithmSeriesResult(
            values=values, time_s=[frame.time_s for frame in frames],
            unit=self.manifest.output_schema["fields"][0]["unit"],
            channel="/".join(self.requested_channels(config)), windows=windows,
            quality=qualities, failures=failures, states=states,
            point_evidence=point_evidence,
            output_values=output_values if any(output_values) else [],
            evidence={"extensions": {"tnk_evidence": {
                "algorithm_id": self.manifest.algorithm_id,
                "window_s": window_s,
                "step_s": step_s,
                "preprocessing_scope": "tnk_window_scoped_v1",
                "preprocessing_version": "tnk-window-spectral-v1",
                "filter_context_s": 2.0,
                "boundary_policy": "clamp_to_recording",
            }}},
        )


def build_tnk_child_adapters() -> tuple[TnkRuntimeAdapter, ...]:
    from . import build_stateful_modules, build_stateless_modules
    from .segment import CompositeMeditationModule, FaaModule
    from app.algorithms.capabilities import tnk_dynamic_capabilities

    modules = (*build_stateless_modules(), *build_stateful_modules(), FaaModule(), CompositeMeditationModule())
    adapters: list[AlgorithmModule] = []
    for module in modules:
        source = module.manifest
        from app.algorithms.capabilities import declared_output_contract
        fields = [{"name": source.algorithm_id, "unit": source.output_unit, "meaning": source.display_name_zh}]
        manifest = AlgorithmManifest(
            algorithm_id=source.algorithm_id, display_name_zh=f"TNK {source.display_name_zh.removeprefix('TNK ')}",
            abbreviation=source.algorithm_id.upper(), purpose_zh=source.display_name_zh,
            scientific_version=source.scientific_version, is_current_version=True,
            implementation_identity=source.implementation_identity,
            supported_modes=["static"] if source.algorithm_id == "tnk_faa" else ["static", "dynamic"],
            output_schema={"fields": fields, "window_shapes": {source.algorithm_id: [1]}},
            signal_requirements=SignalRequirements(
                input_roles=list(source.channel_roles), representation="spectrum",
                source_shape="single" if len(source.channel_roles) == 1 else "paired",
                preprocessing_scope="configured_continuous_before_slice",
            ),
            execution_kind="generic_research_primitives", availability="available", is_runnable=True,
            required_channel_roles=list(source.channel_roles),
            catalog_group="TNK 脑指标",
            dynamic_capabilities=tnk_dynamic_capabilities(
                segment_only=source.algorithm_id == "tnk_faa",
            ),
            definition_persistence={"name": f"TNK {source.algorithm_id}", "version": "1.0.0"},
        )
        manifest.output_contract = declared_output_contract(
            manifest, static_result_type="scalar", dynamic_result_type="series",
            evidence_fields=["tnk_evidence"],
        )
        adapter = TnkRuntimeAdapter(module, manifest)
        adapters.append(adapter)
    from .legacy_runtime import build_tnk_legacy_adapters
    return tuple(adapters) + build_tnk_legacy_adapters()


def build_tnk_runtime_adapters() -> tuple[AlgorithmModule, ...]:
    """Expose the TNK family through one public runtime/catalog entry."""
    from .group import TnkBiofeedbackGroupAdapter

    return (TnkBiofeedbackGroupAdapter(build_tnk_child_adapters()),)
