"""Public TNK group adapter; child science is owned by registered modules."""

from __future__ import annotations

from typing import Any

import numpy as np
from pydantic import model_validator

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmInputs,
    AlgorithmManifest,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    CapabilityEvidenceStatus,
    DynamicAnalysisPolicy,
    DynamicStrategyCapabilities,
    SignalRequirements,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.errors import UnsupportedAlgorithmModeError
from app.algorithm_runtime.group_execution import AlgorithmGroupExecutor
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterSchema
from app.algorithm_runtime.windows import build_dynamic_analysis_frames, controlled_frames

from .group_graph import TNK_ROLE_ORDER, build_tnk_group_graph
from .group_results import (
    analysis_state,
    group_failure,
    metric_values,
    point_evidence,
    quality as group_quality,
)
from .group_scheduler import execute_tnk_frame, fresh_tnk_children, reset_tnk_children
from .group_window import TnkGroupWindowCapability

_ROLES = TNK_ROLE_ORDER


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
    """Coordinate TNK graph execution without owning child formulas."""

    config_model = TnkGroupConfig
    on_demand_default_enabled = True
    window_policy_version = "tnk-biofeedback-dynamic-window-v4-graph-execution"

    def __init__(self, children: tuple[Any, ...]) -> None:
        super().__init__()
        self.group_graph = build_tnk_group_graph(children)
        children_by_id = {child.manifest.algorithm_id: child for child in children}
        self.children = tuple(children_by_id[node.node_id] for node in self.group_graph.nodes)
        self._child_by_id = {child.manifest.algorithm_id: child for child in self.children}
        self._metric_units = self._build_metric_units()
        metric_count = len(self._metric_units)
        fields = [{"name": "metric_index", "unit": "index", "meaning": "TNK 子指标顺序"}]
        fields.extend({"name": name, "unit": unit, "meaning": name} for name, unit in self._metric_units.items())
        self.manifest = AlgorithmManifest(
            algorithm_id="tnk_biofeedback", display_name_zh="TNK 脑指标",
            abbreviation="TNK", purpose_zh="统一来源通道映射的 TNK 脑指标组",
            scientific_version="tnk-biofeedback-group-v2", is_current_version=True,
            implementation_identity=f"tnk-biofeedback-group-{self.group_graph.identity}",
            supported_modes=["dynamic"],
            dynamic_policy=DynamicAnalysisPolicy(
                minimum_window_s=4.0,
                window_options_s=[4.0],
                default_window_s=4.0,
                refresh_step_s=1.0,
                refresh_step_options_s=[1.0],
                allow_warmup=True,
            ),
            output_schema={
                "fields": fields,
                "window_shapes": {
                    item["name"]: [metric_count] if item["name"] == "metric_index" else [1]
                    for item in fields
                },
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
                    reason="组级执行图与结果契约已有测试；完整真实录音验收尚未完成。",
                    evidence=["backend/tests/test_tnk_group.py"],
                ),
                on_demand=CapabilityEvidenceStatus(
                    implementation="implemented", validation="unvalidated",
                    reason="目标窗回放使用相同组帧执行器；真实录音验收尚未完成。",
                    evidence=["backend/tests/test_tnk_group.py"],
                ),
                combined_preview_background_full=CapabilityEvidenceStatus(
                    implementation="implemented", validation="unvalidated",
                    reason="组级 combined parity 尚待验收。",
                ),
                full_result_to_preview=CapabilityEvidenceStatus(
                    implementation="unsupported", validation="not_applicable",
                    reason="组结果不能从单个标量预览中反向投影。",
                ),
                on_demand_result_to_full=CapabilityEvidenceStatus(
                    implementation="unsupported", validation="not_applicable",
                    reason="组级精确窗口反向复用未声明。",
                ),
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
                units.update({
                    f"tnk_rbp.{role}.{band}": unit
                    for role in _ROLES for band in child.module.bands
                })
            else:
                units[algorithm_id] = unit
        return units

    def _child_catalog_contract(self) -> list[dict[str, Any]]:
        """Expose child requirements without making child nodes selectable."""
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
        labels = {
            str(item).casefold(): str(item)
            for item in getattr(recording, "channel_names", getattr(recording, "channels", []))
        }
        missing = [
            value for value in self.requested_channels(config)
            if value.casefold() not in labels
        ]
        if missing:
            raise ValueError(f"TNK 来源通道在当前记录/导联快照中不存在: {missing}")
        channels = [labels[value.casefold()] for value in self.requested_channels(config)]
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=channels[0],
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    def execute_static(self, inputs: Any, config: TnkGroupConfig) -> AlgorithmStructuredResult:
        raise UnsupportedAlgorithmModeError(
            "TNK 脑指标组仅支持动态分析。",
            detail={"algorithm_id": self.manifest.algorithm_id, "mode": config.mode,
                    "supported_modes": self.manifest.supported_modes},
        )

    def execute_dynamic(self, inputs: Any, config: TnkGroupConfig) -> AlgorithmStructuredSeriesResult:
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=4.0, step_s=1.0, minimum_window_s=4.0, allow_warmup=True,
        )
        if not frames:
            raise ValueError("TNK 脑指标组没有可执行动态窗口")
        children = fresh_tnk_children(self)
        reset_tnk_children(children)
        values_by_metric = {name: [] for name in self._metric_units}
        windows: list[StructuredSeriesWindow] = []
        points = []
        with AlgorithmGroupExecutor(self.group_graph) as executor:
            for frame in controlled_frames(frames, inputs.execution_context):
                child_results = execute_tnk_frame(
                    self, executor, inputs, config, frame.window_start_s, frame.window_end_s,
                    children=children,
                )
                values, evidence = metric_values(self._metric_units, child_results)
                for name, value in values.items():
                    values_by_metric[name].append([np.nan if value is None else value])
                quality = group_quality(child_results)
                point = point_evidence(self, config, evidence, values)
                windows.append(StructuredSeriesWindow(
                    start_sample=int(round(frame.window_start_s * inputs.sfreq_hz)),
                    end_sample=int(round(frame.window_end_s * inputs.sfreq_hz)),
                    start_s=frame.window_start_s, end_s=frame.window_end_s,
                    state=analysis_state(quality), quality=quality,
                    failure=group_failure(quality), evidence=point,
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
                "preprocessing_scope": "tnk_window_scoped_v1",
                "preprocessing_version": "tnk-window-spectral-v1",
                "filter_context_s": 2.0,
                "boundary_policy": "clamp_to_recording",
                "execution_graph": {
                    "version": self.group_graph.version,
                    "identity": self.group_graph.identity,
                },
            }}},
        )
