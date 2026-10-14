from __future__ import annotations

from collections.abc import Callable
from typing import Any

import numpy as np

from app.algorithm_runtime.contracts import (
    AlgorithmConfigBase,
    AlgorithmExecutionSnapshot,
    AlgorithmFailure,
    AlgorithmInputs,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
    StructuredSeriesWindow,
)
from app.algorithm_runtime.on_demand import (
    DynamicSessionIdentity,
    DynamicWindowIdentity,
    DynamicWindowPlan,
    DynamicWindowResult,
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from app.algorithm_runtime.windows import (
    build_dynamic_analysis_frames,
    controlled_frames,
    latest_dynamic_analysis_frame_at_or_before,
)
from app.scientific.contracts.types import SampleRange
from app.scientific.quality import SpectralQualityGateError

from .config import PsdConfig
from .manifest import MANIFEST


class PsdAlgorithm:
    manifest = MANIFEST
    config_model = PsdConfig
    output_schema = MANIFEST.output_schema
    BAND_SHARE_LABELS = ("delta", "theta", "alpha", "beta", "gamma")
    BAND_SHARE_RANGES = ((1.0, 4.0), (4.0, 8.0), (8.0, 13.0), (13.0, 30.0), (30.0, 50.0))
    window_policy_version = "psd-dynamic-window-v1"

    def can_execute_window(self, config: AlgorithmConfigBase) -> bool:
        """PSD is opt-in capable only for its validated dynamic configuration."""
        return isinstance(config, PsdConfig) and config.mode == "dynamic"

    def plan_window(self, inputs: AlgorithmInputs, config: PsdConfig, cursor_sample: int,
                    session_identity: DynamicSessionIdentity) -> DynamicWindowPlan:
        """Resolve the latest endpoint-aligned PSD frame already reached."""
        if not self.can_execute_window(config):
            raise ValueError("PSD on-demand capability requires dynamic mode")
        if cursor_sample < 0:
            raise ValueError("cursor sample must be non-negative")
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        cursor_s = cursor_sample / inputs.sfreq_hz
        frame = latest_dynamic_analysis_frame_at_or_before(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s, cursor_s=cursor_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if frame is None:
            start_s = min(max(cursor_s, 0.0), inputs.duration_s)
            end_s = min(inputs.duration_s, start_s + 1.0 / inputs.sfreq_hz)
            if end_s <= start_s:
                start_s = max(0.0, inputs.duration_s - 1.0 / inputs.sfreq_hz)
                end_s = inputs.duration_s
            return DynamicWindowPlan(
                session_identity=session_identity, cursor_sample=cursor_sample, cursor_s=cursor_s,
                start_sample=int(np.floor(start_s * inputs.sfreq_hz)),
                end_sample=max(int(np.ceil(end_s * inputs.sfreq_hz)), int(np.floor(start_s * inputs.sfreq_hz)) + 1),
                start_s=start_s, end_s=end_s,
                state="insufficient_data", reason="cursor has no policy-eligible PSD frame",
            )
        sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
        return DynamicWindowPlan(
            session_identity=session_identity, cursor_sample=cursor_sample, cursor_s=cursor_s,
            start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
            start_s=frame.window_start_s, end_s=frame.window_end_s,
        )

    def execute_window(self, inputs: AlgorithmInputs, config: PsdConfig,
                       plan: DynamicWindowPlan, identity: DynamicWindowIdentity,
                       check_cancelled: Callable[[], None] | None = None) -> DynamicWindowResult:
        """Execute exactly one PSD frame through the existing full-record source cache."""
        fixed_frequencies = self._frequency_axis(inputs.sfreq_hz, 1.0, 50.0)
        if plan.state != "planned":
            return DynamicWindowResult(
                identity=identity, state="insufficient_data", output_kind="frequency_series",
                axes={"frequency_hz": fixed_frequencies}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": np.full(fixed_frequencies.shape, np.nan), "band_share": np.full(5, np.nan)},
                array_units={"psd": "V^2/Hz", "band_share": "ratio"}, quality="insufficient_data",
                failure={"code": "PSD_WINDOW_INSUFFICIENT_DATA", "message": plan.reason or "window unavailable"},
            )
        if check_cancelled is not None:
            check_cancelled()
        try:
            spectrum = self._load(
                inputs.payload, channel=inputs.channel, start_s=plan.start_s,
                window_s=plan.end_s - plan.start_s,
                filter_low_hz=config.low_hz, filter_high_hz=config.high_hz,
                output_low_hz=1.0, output_high_hz=50.0, notch_hz=config.notch_hz,
            )
            if check_cancelled is not None:
                check_cancelled()
            frequencies = np.asarray(spectrum.freqs, dtype=float)
            values = np.asarray(spectrum.psd[0], dtype=float)
            evidence = self._dynamic_evidence(spectrum, config)
            quality = evidence["source_quality"]
            if spectrum.gate_failed:
                raise SpectralQualityGateError(quality)
            return DynamicWindowResult(
                identity=identity, state="ready", output_kind="frequency_series",
                axes={"frequency_hz": frequencies}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": values, "band_share": self._band_shares(frequencies, values)},
                array_units={"psd": "V^2/Hz", "band_share": "ratio"}, quality="clean",
                evidence=evidence, provenance={"preprocessing_scope": "whole_recording_before_window_slice"},
            )
        except SpectralQualityGateError as exc:
            return DynamicWindowResult(
                identity=identity, state="rejected", output_kind="frequency_series",
                axes={"frequency_hz": fixed_frequencies}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": np.full(fixed_frequencies.shape, np.nan), "band_share": np.full(5, np.nan)},
                array_units={"psd": "V^2/Hz", "band_share": "ratio"}, quality="gate_failed",
                evidence={"source_quality": dict(exc.quality), "spectral_evidence": dict(exc.quality.get("evidence", {})),
                          "calculation_trace": {"algorithm": "welch_psd", "window_policy_version": self.window_policy_version}},
                provenance={"preprocessing_scope": "whole_recording_before_window_slice"},
                failure={"code": "PSD_WINDOW_QUALITY_GATE_FAILED", "message": "当前动态窗口未通过 PSD 质量门", "details": dict(exc.quality)},
            )
        except ValueError as exc:
            return DynamicWindowResult(
                identity=identity, state="unavailable", output_kind="frequency_series",
                axes={"frequency_hz": fixed_frequencies}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": np.full(fixed_frequencies.shape, np.nan), "band_share": np.full(5, np.nan)},
                array_units={"psd": "V^2/Hz", "band_share": "ratio"}, quality="unavailable",
                failure={"code": "PSD_WINDOW_UNAVAILABLE", "message": str(exc)},
            )

    @classmethod
    def _band_shares(cls, frequencies: np.ndarray, values: np.ndarray) -> np.ndarray:
        frequencies = np.asarray(frequencies, dtype=float)
        values = np.asarray(values, dtype=float)
        powers = np.full(5, np.nan, dtype=float)
        full_range_available = (
            frequencies.size >= 2
            and frequencies[0] <= cls.BAND_SHARE_RANGES[0][0]
            and frequencies[-1] >= cls.BAND_SHARE_RANGES[-1][1]
        )
        if not full_range_available:
            return powers
        for index, (low_hz, high_hz) in enumerate(cls.BAND_SHARE_RANGES):
            mask = (frequencies >= low_hz) & (frequencies <= high_hz)
            if mask.sum() < 2:
                return np.full(5, np.nan, dtype=float)
            powers[index] = float(np.trapezoid(values[mask], frequencies[mask]))
        finite = np.isfinite(powers)
        total = float(np.sum(powers[finite]))
        if total <= 0:
            return np.full(5, np.nan, dtype=float)
        return powers / total

    @classmethod
    def _band_share_trace(cls) -> dict[str, Any]:
        return {"band_share_labels": list(cls.BAND_SHARE_LABELS), "band_share_ranges_hz": [list(item) for item in cls.BAND_SHARE_RANGES]}

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="low_hz", label_zh="高通截止频率", value_type="number", unit="Hz", minimum=0, step=0.1),
            AlgorithmParameter(key="high_hz", label_zh="低通截止频率", value_type="number", unit="Hz", minimum=0, default=50.0, step=0.1),
            AlgorithmParameter(key="notch_hz", label_zh="陷波频率", value_type="enum", required=False, default=50.0, options=[ParameterOption(value=0.0, label_zh="关闭"), ParameterOption(value=50.0, label_zh="50 Hz"), ParameterOption(value=60.0, label_zh="60 Hz")]),
            AlgorithmParameter(key="window_s", label_zh="动态窗口", value_type="number", unit="s", minimum=4, step=1),
            AlgorithmParameter(key="step_s", label_zh="动态步长", value_type="number", unit="s", minimum=1, step=1),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        return spectral_execution_snapshot(config, low_hz=config.low_hz,
                                           high_hz=config.high_hz, notch_hz=config.notch_hz)

    def resolve_inputs(self, recording: Any, config: PsdConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if config.end_s > float(recording.duration_s):
            raise ValueError("PSD 分析范围超出记录时长")
        if config.end_s - config.start_s < 4.0:
            raise ValueError("PSD 分析区间至少需要 4 秒")
        if config.high_hz >= float(recording.sfreq_hz) / 2.0:
            raise ValueError(f"PSD 最高频率必须低于奈奎斯特频率（{float(recording.sfreq_hz) / 2:g}Hz）")
        if config.mode == "dynamic" and float(recording.sfreq_hz) / 2.0 <= 50.0:
            raise ValueError("PSD 动态趋势轴固定为 1–50 Hz，记录奈奎斯特频率必须高于 50 Hz")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=selected,
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float, filter_low_hz: float, filter_high_hz: float, output_low_hz: float, output_high_hz: float, notch_hz: float | None) -> Any:
        return recording.load_spectrum(
            start_s=start_s, window_s=window_s, channels=[channel],
            filter_low_hz=filter_low_hz, filter_high_hz=filter_high_hz,
            output_low_hz=output_low_hz, output_high_hz=output_high_hz,
            notch_hz=notch_hz,
        )

    def execute_static(self, inputs: AlgorithmInputs, config: PsdConfig) -> AlgorithmStructuredResult:
        spectrum = self._load(
            inputs.payload, channel=inputs.channel, start_s=config.start_s,
            window_s=config.end_s - config.start_s,
            filter_low_hz=config.low_hz, filter_high_hz=config.high_hz,
            output_low_hz=config.low_hz, output_high_hz=config.high_hz,
            notch_hz=config.notch_hz,
        )
        requested = {"start_s": config.start_s, "end_s": config.end_s}
        source_quality = {
            "clean_segments": spectrum.clean_epochs,
            "total_segments": spectrum.total_epochs,
            "clean_ratio": spectrum.signal_quality,
            "gate_failed": spectrum.gate_failed,
            "rejected_reasons": list(spectrum.rejected_reasons),
        }
        spectral_evidence = dict(getattr(spectrum, "evidence", {}))
        if isinstance(spectral_evidence.get("amplitude"), dict):
            source_quality["amplitude"] = spectral_evidence["amplitude"]
        evidence = {
            "source_quality": source_quality,
            "spectral_evidence": spectral_evidence,
            "calculation_trace": {"algorithm": "welch_psd"},
        }
        if spectrum.gate_failed:
            failure = AlgorithmFailure(
                code="PSD_QUALITY_GATE_FAILED",
                message="当前分析区间未通过 PSD 质量门",
                detail=source_quality,
            )
            return AlgorithmStructuredResult(
                output_kind="frequency_series", channel_order=[inputs.channel],
                axes={"frequency_hz": np.asarray([], dtype=float)}, axis_units={"frequency_hz": "Hz"},
                arrays={"psd": np.empty((0,), dtype=float), "band_share": np.full(5, np.nan, dtype=float)}, array_units={"psd": "V^2/Hz", "band_share": "ratio"},
                requested_range=requested, actual_range=requested, quality="gate_failed",
                failure=failure, evidence=evidence,
            )
        values = np.asarray(spectrum.psd[0], dtype=float)
        band_share = self._band_shares(np.asarray(spectrum.freqs, dtype=float), values)
        evidence["calculation_trace"] = {"algorithm": "welch_psd", **self._band_share_trace()}
        return AlgorithmStructuredResult(
            output_kind="frequency_series", channel_order=[inputs.channel],
            axes={"frequency_hz": np.asarray(spectrum.freqs, dtype=float)}, axis_units={"frequency_hz": "Hz"},
            arrays={"psd": values, "band_share": band_share}, array_units={"psd": "V^2/Hz", "band_share": "ratio"},
            requested_range=requested, actual_range=requested, quality="clean", evidence=evidence,
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: PsdConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("PSD 动态分析区间没有可执行窗口")

        rows: list[np.ndarray] = []
        band_share_rows: list[np.ndarray] = []
        windows: list[StructuredSeriesWindow] = []
        # Welch's segment size is fixed at four seconds. Dynamic warm-up
        # windows may be rejected, but they must not decide a different matrix
        # shape from later complete windows.
        # Dynamic PSD is a time trend over a stable display axis. Its axis is
        # intentionally independent from the static PSD range controls.
        fixed_trend_axis = True
        trend_low_hz, trend_high_hz = 1.0, 50.0
        if trend_high_hz >= inputs.sfreq_hz / 2.0:
            raise ValueError("动态 PSD 的固定趋势范围 1–50 Hz 超出奈奎斯特频率")
        frequencies = self._frequency_axis(inputs.sfreq_hz, trend_low_hz, trend_high_hz)
        for frame in controlled_frames(frames, inputs.execution_context):
            sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
            failure = None
            quality = "clean"
            state = frame.state
            evidence: dict[str, Any] = {}
            try:
                spectrum = self._load(
                    inputs.payload, channel=inputs.channel,
                    start_s=frame.window_start_s, window_s=frame.actual_window_s,
                    filter_low_hz=config.low_hz, filter_high_hz=config.high_hz,
                    output_low_hz=trend_low_hz, output_high_hz=trend_high_hz,
                    notch_hz=config.notch_hz,
                )
                current_frequencies = np.asarray(spectrum.freqs, dtype=float)
                if not np.array_equal(frequencies, current_frequencies):
                    raise ValueError("PSD 动态窗口返回的频率轴与本次 Welch 合同不一致")
                row = np.asarray(spectrum.psd[0], dtype=float)
                band_share_rows.append(self._band_shares(current_frequencies, row))
                evidence = self._dynamic_evidence(spectrum, config)
            except SpectralQualityGateError as exc:
                row = np.full(frequencies.shape, np.nan, dtype=float)
                band_share_rows.append(np.full(5, np.nan, dtype=float))
                quality = "gate_failed"
                state = "Rejected"
                evidence = {"source_quality": dict(exc.quality), "spectral_evidence": dict(exc.quality.get("evidence", {}))}
                failure = AlgorithmFailure(
                    code="PSD_WINDOW_QUALITY_GATE_FAILED",
                    message="当前动态窗口未通过 PSD 质量门",
                    detail=dict(exc.quality),
                )
            except ValueError as exc:
                row = np.full(frequencies.shape, np.nan, dtype=float)
                band_share_rows.append(np.full(5, np.nan, dtype=float))
                quality = "unavailable"
                state = "Unavailable"
                failure = AlgorithmFailure(code="PSD_WINDOW_UNAVAILABLE", message=str(exc))
            rows.append(row)
            windows.append(StructuredSeriesWindow(
                start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
                start_s=frame.window_start_s, end_s=frame.window_end_s,
                state=state, quality=quality, failure=failure, evidence=evidence,
            ))

        state_values = {window.state for window in windows}
        overall = "clean" if state_values == {"Complete"} else "partial"
        return AlgorithmStructuredSeriesResult(
            output_kind="frequency_series", channel_order=[inputs.channel],
            axes={"frequency_hz": frequencies}, axis_units={"frequency_hz": "Hz"},
            arrays={"psd": np.stack(rows), "band_share": np.stack(band_share_rows)}, array_units={"psd": "V^2/Hz", "band_share": "ratio"},
            windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": windows[0].start_s, "end_s": windows[-1].end_s},
            quality=overall,
            evidence={
                "source_quality": {}, "spectral_evidence": {},
                "calculation_trace": {
                    "algorithm": "welch_psd", "window_s": window_s, "step_s": step_s,
                    "trend_frequency_range_hz": [float(trend_low_hz), float(trend_high_hz)] if fixed_trend_axis else None,
                    **self._band_share_trace(),
                },
            },
        )

    @staticmethod
    def _dynamic_evidence(spectrum: Any, config: PsdConfig) -> dict[str, Any]:
        spectral = dict(getattr(spectrum, "evidence", {}))
        quality = {"clean_segments": spectrum.clean_epochs, "total_segments": spectrum.total_epochs,
                   "clean_ratio": spectrum.signal_quality, "gate_failed": spectrum.gate_failed,
                   "rejected_reasons": list(spectrum.rejected_reasons)}
        if isinstance(spectral.get("amplitude"), dict):
            quality["amplitude"] = spectral["amplitude"]
        return {"source_quality": quality, "spectral_evidence": spectral,
                "calculation_trace": {
                    "filter_frequency_range_hz": {"low_hz": config.low_hz, "high_hz": config.high_hz},
                    "output_frequency_range_hz": {"low_hz": 1.0, "high_hz": 50.0}}}

    @staticmethod
    def _frequency_axis(sfreq_hz: float, low_hz: float = 1.0, high_hz: float = 30.0) -> np.ndarray:
        full = np.fft.rfftfreq(int(round(4.0 * sfreq_hz)), 1.0 / sfreq_hz)
        return full[(full >= low_hz) & (full <= high_hz)]
