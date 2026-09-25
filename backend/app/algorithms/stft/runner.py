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
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot
from app.algorithm_runtime.windows import build_dynamic_analysis_frames
from app.scientific.contracts.types import SampleRange
from app.scientific.quality import SpectralQualityGateError

from .config import StftConfig
from .manifest import MANIFEST


class StftAlgorithm:
    manifest = MANIFEST
    config_model = StftConfig
    output_schema = MANIFEST.output_schema

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态"), ParameterOption(value="dynamic", label_zh="动态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="window_s", label_zh="动态窗口", value_type="number", unit="s", minimum=4, step=1),
            AlgorithmParameter(key="step_s", label_zh="动态步长", value_type="number", unit="s", minimum=1, step=1),
        ])

    def requested_channels(self, config: AlgorithmConfigBase) -> list[str]:
        return [config.channel]

    def execution_snapshot(self, config: AlgorithmConfigBase) -> AlgorithmExecutionSnapshot:
        snapshot = spectral_execution_snapshot(config)
        snapshot.window.update({"segment_s": 4.0, "step_s": 1.0, "time_axis": "window_center"})
        snapshot.quality_rules["spectrogram_contract_version"] = "spectrogram-v2"
        return snapshot

    def resolve_inputs(self, recording: Any, config: StftConfig) -> AlgorithmInputs:
        labels = list(getattr(recording, "channel_names", getattr(recording, "channels", [])))
        selected = next((str(label) for label in labels if str(label).casefold() == config.channel.casefold()), None)
        if selected is None:
            raise ValueError(f"未知原始通道: {config.channel}")
        if config.end_s > float(recording.duration_s):
            raise ValueError("STFT 分析范围超出记录时长")
        if config.end_s - config.start_s < 4.0:
            raise ValueError("STFT 分析区间至少需要 4 秒")
        return AlgorithmInputs(
            recording_id=str(getattr(recording, "id", "unknown")), channel=selected,
            sfreq_hz=float(recording.sfreq_hz), duration_s=float(recording.duration_s), payload=recording,
        )

    @staticmethod
    def _load(recording: Any, *, channel: str, start_s: float, window_s: float) -> dict[str, object]:
        loader: Callable[..., dict[str, object]] = getattr(recording, "load_spectrogram")
        return loader(start_s=start_s, window_s=window_s, channels=[channel])

    def execute_static(self, inputs: AlgorithmInputs, config: StftConfig) -> AlgorithmStructuredResult:
        payload = self._load(
            inputs.payload, channel=inputs.channel, start_s=config.start_s,
            window_s=config.end_s - config.start_s,
        )
        quality = dict(payload["quality"])
        windows = list(quality.get("windows", []))
        bad_windows = int(quality.get("bad_windows", 0))
        requested = {"start_s": config.start_s, "end_s": config.end_s}
        times = np.asarray(payload["times_s"], dtype=float)
        frequencies = np.asarray(payload["frequencies_hz"], dtype=float)
        linear_uv = np.asarray(payload["power_linear"][inputs.channel], dtype=float)
        power_db = np.asarray(payload["power_db"][inputs.channel], dtype=float)
        evidence = {
            "source_quality": quality,
            "spectral_evidence": dict(quality.get("evidence", {})),
            "calculation_trace": {
                "spectrogram_contract_version": payload["spectrogram_contract_version"],
                "segment_s": payload["segment_s"], "step_s": payload["step_s"],
                "window_quality_rows": windows,
            },
        }
        failure = None
        status = "clean"
        if bad_windows == len(windows) and windows:
            status = "gate_failed"
            failure = AlgorithmFailure(
                code="STFT_QUALITY_GATE_FAILED",
                message="当前时频分析区间没有可用窗口",
                detail={"bad_windows": bad_windows, "total_windows": len(windows)},
            )
        elif bad_windows:
            status = "partial"
        return AlgorithmStructuredResult(
            output_kind="time_frequency", channel_order=[inputs.channel],
            axes={"time_center_s": times, "frequency_hz": frequencies},
            axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
            arrays={
                "power_linear": linear_uv * 1e-12,
                "power_db": power_db,
            },
            array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
            requested_range=requested,
            actual_range={"start_s": float(payload["window_start_s"]), "end_s": float(payload["window_start_s"]) + float(payload["window_duration_s"])},
            quality=status, failure=failure, evidence=evidence,
        )

    def execute_dynamic(self, inputs: AlgorithmInputs, config: StftConfig) -> AlgorithmStructuredSeriesResult:
        window_s = config.window_s or self.manifest.dynamic_policy.default_window_s
        step_s = config.step_s or self.manifest.dynamic_policy.refresh_step_s
        frames = build_dynamic_analysis_frames(
            config.start_s, config.end_s, duration_s=inputs.duration_s,
            window_s=window_s, step_s=step_s,
            minimum_window_s=self.manifest.dynamic_policy.minimum_window_s,
            allow_warmup=self.manifest.dynamic_policy.allow_warmup,
        )
        if not frames:
            raise ValueError("STFT 动态分析区间没有可执行窗口")

        inner_count = int(np.floor(window_s - 4.0) + 1)
        inner_times = 2.0 + np.arange(inner_count, dtype=float)
        rows_linear: list[np.ndarray] = []
        rows_db: list[np.ndarray] = []
        windows: list[StructuredSeriesWindow] = []
        frequencies: np.ndarray | None = None
        for frame in frames:
            sample_range = SampleRange.from_seconds(frame.window_start_s, frame.window_end_s, inputs.sfreq_hz)
            failure = None
            quality = "clean"
            state = frame.state
            evidence: dict[str, Any] = {"calculation_trace": {"inner_valid_count": 0}}
            try:
                payload = self._load(
                    inputs.payload, channel=inputs.channel,
                    start_s=frame.window_start_s, window_s=frame.actual_window_s,
                )
                current_frequencies = np.asarray(payload["frequencies_hz"], dtype=float)
                if frequencies is None:
                    frequencies = current_frequencies
                elif not np.array_equal(frequencies, current_frequencies):
                    raise RuntimeError("STFT 动态窗口的频率轴不一致")
                quality_payload = dict(payload["quality"])
                relative_times = np.asarray(payload["times_s"], dtype=float) - frame.window_start_s
                linear_source = np.asarray(payload["power_linear"][inputs.channel], dtype=float) * 1e-12
                db_source = np.asarray(payload["power_db"][inputs.channel], dtype=float)
                valid_count = min(len(relative_times), inner_count)
                linear = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                db = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                linear[:valid_count] = linear_source[:valid_count]
                db[:valid_count] = db_source[:valid_count]
                evidence = {
                    "source_quality": quality_payload,
                    "spectral_evidence": dict(quality_payload.get("evidence", {})),
                    "calculation_trace": {"inner_valid_count": valid_count},
                }
                if int(quality_payload.get("bad_windows", 0)):
                    quality = "gate_failed"
                    state = "Rejected"
                    failure = AlgorithmFailure(
                        code="STFT_WINDOW_QUALITY_GATE_FAILED",
                        message="当前动态窗口包含未通过质量门的时频窗",
                        detail={"bad_windows": int(quality_payload["bad_windows"]), "total_windows": int(quality_payload["total_windows"])},
                    )
            except SpectralQualityGateError as exc:
                frequencies = frequencies if frequencies is not None else self._frequency_axis(inputs.sfreq_hz)
                linear = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                db = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                quality = "gate_failed"
                state = "Rejected"
                evidence = {"source_quality": dict(exc.quality), "spectral_evidence": dict(exc.quality.get("evidence", {})), "calculation_trace": {"inner_valid_count": 0}}
                failure = AlgorithmFailure(code="STFT_WINDOW_QUALITY_GATE_FAILED", message="当前动态窗口未通过 STFT 质量门", detail=dict(exc.quality))
            except ValueError as exc:
                frequencies = frequencies if frequencies is not None else self._frequency_axis(inputs.sfreq_hz)
                linear = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                db = np.full((inner_count, len(frequencies)), np.nan, dtype=float)
                quality = "unavailable"
                state = "Unavailable"
                evidence = {"calculation_trace": {"inner_valid_count": 0}}
                failure = AlgorithmFailure(code="STFT_WINDOW_UNAVAILABLE", message=str(exc))
            rows_linear.append(linear)
            rows_db.append(db)
            windows.append(StructuredSeriesWindow(
                start_sample=sample_range.start_sample, end_sample=sample_range.end_sample,
                start_s=frame.window_start_s, end_s=frame.window_end_s,
                state=state, quality=quality, failure=failure, evidence=evidence,
            ))

        assert frequencies is not None
        state_values = {window.state for window in windows}
        overall = "clean" if state_values == {"Complete"} else "partial"
        return AlgorithmStructuredSeriesResult(
            output_kind="time_frequency", channel_order=[inputs.channel],
            axes={"time_center_s": inner_times, "frequency_hz": frequencies},
            axis_units={"time_center_s": "s", "frequency_hz": "Hz"},
            arrays={"power_linear": np.stack(rows_linear), "power_db": np.stack(rows_db)},
            array_units={"power_linear": "V^2/Hz", "power_db": "dB re 1 uV^2/Hz"},
            windows=windows,
            requested_range={"start_s": config.start_s, "end_s": config.end_s},
            actual_range={"start_s": windows[0].start_s, "end_s": windows[-1].end_s},
            quality=overall,
            evidence={"source_quality": {}, "spectral_evidence": {}, "calculation_trace": {"spectrogram_contract_version": "spectrogram-v2", "window_s": window_s, "step_s": step_s, "inner_time_padding": "nan_unavailable"}},
        )

    @staticmethod
    def _frequency_axis(sfreq_hz: float) -> np.ndarray:
        full = np.fft.rfftfreq(int(round(4.0 * sfreq_hz)), 1.0 / sfreq_hz)
        return full[(full >= 1.0) & (full <= 30.0)]
