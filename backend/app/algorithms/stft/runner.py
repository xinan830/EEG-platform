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
)
from app.algorithm_runtime.parameter_schema import AlgorithmParameter, ParameterOption, ParameterSchema
from app.algorithms.spectral_snapshot import spectral_execution_snapshot

from .config import StftConfig
from .manifest import MANIFEST


class StftAlgorithm:
    manifest = MANIFEST
    config_model = StftConfig
    output_schema = MANIFEST.output_schema

    def parameter_schema(self) -> ParameterSchema:
        return ParameterSchema(parameters=[
            AlgorithmParameter(key="channel", label_zh="分析通道", value_type="string", description_zh="从原始记录通道中选择一个通道。"),
            AlgorithmParameter(key="mode", label_zh="分析模式", value_type="enum", options=[ParameterOption(value="static", label_zh="静态")]),
            AlgorithmParameter(key="start_s", label_zh="分析开始", value_type="number", unit="s", minimum=0, step=0.001),
            AlgorithmParameter(key="end_s", label_zh="分析结束", value_type="number", unit="s", minimum=0, step=0.001),
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
