"""Traceable synchronous execution facade for existing backend analyses."""

from __future__ import annotations

from pathlib import Path
from typing import Any
from uuid import uuid4

import numpy as np

from app.core.config import ARTIFACTS_DIR, DATABASE_PATH
from app.core.provenance import (
    build_cache_key,
    execution_environment,
    implementation_version,
    sha256_json,
)
from app.eeg_core.analysis_contract import ANALYSIS_CONTRACT, ANALYSIS_ALGORITHM_VERSION
from app.eeg_core.quality import SpectralQualityGateError
from app.models.analysis_config import AnalysisConfigRequest
from app.models.run import AnalysisRun, RunCreateRequest, RunStatus, StructuredRunError
from app.processing.offline_analysis import analyze_recording
from app.services.artifacts import ArtifactStore
from app.services.recordings import RecordingService
from app.services.run_repository import RunRepository, utc_now


class RunConflictError(RuntimeError):
    pass


class RunService:
    def __init__(
        self,
        recordings: RecordingService,
        database_path: Path = DATABASE_PATH,
        artifacts_dir: Path = ARTIFACTS_DIR,
    ):
        self.recordings = recordings
        self.repository = RunRepository(database_path)
        self.artifacts = ArtifactStore(self.repository, artifacts_dir)

    def create(self, request: RunCreateRequest) -> AnalysisRun:
        recording = self.recordings.require_recording(request.recording_id)
        if not recording.source_sha256:
            raise ValueError("recording source identity is unavailable")
        resolved = self._resolve_request(request, recording)
        config_sha256 = sha256_json(resolved["config"])
        build = implementation_version()
        cache_key = build_cache_key(
            source_sha256=recording.source_sha256,
            definition_sha256=resolved["definition_sha256"],
            config_sha256=config_sha256,
            implementation_build=build,
            actual_range=resolved["actual_range"],
        )
        now = utc_now()
        run = AnalysisRun(
            run_id=uuid4().hex,
            recording_id=recording.id,
            analysis_type=request.analysis_type,
            status=RunStatus.QUEUED,
            definition_id=request.definition_id,
            definition_version=request.definition_version,
            scientific_version=resolved["scientific_version"],
            implementation_version=build,
            config=resolved["config"],
            config_sha256=config_sha256,
            cache_key=cache_key,
            requested_range=resolved["requested_range"],
            actual_range=None,
            channel_mapping=resolved["channel_mapping"],
            reference=resolved["reference"],
            filters=resolved["filters"],
            window=resolved["window"],
            quality_rules=resolved["quality_rules"],
            environment=execution_environment(),
            is_preview=request.preview,
            created_at=now,
            updated_at=now,
        )
        self.repository.create(run)
        self.repository.update_status(run.run_id, RunStatus.RUNNING)

        cached = self.repository.find_completed_cache(cache_key)
        if cached is not None and cached.run_id != run.run_id:
            return self.repository.update_status(
                run.run_id,
                RunStatus.COMPLETED,
                actual_range=cached.actual_range,
                result_summary=cached.result_summary,
                reused_from_run_id=cached.run_id,
            )

        try:
            result, arrays, unit = self._execute(request.analysis_type, recording, resolved)
            artifact = self.artifacts.write_npz(run.run_id, request.analysis_type, arrays, unit)
            summary = {**result, "artifacts": [artifact.model_dump(mode="json")]}
            return self.repository.update_status(
                run.run_id,
                RunStatus.COMPLETED,
                actual_range=resolved["actual_range"],
                result_summary=summary,
            )
        except ValueError as exc:
            is_gate = isinstance(exc, SpectralQualityGateError)
            status = RunStatus.GATE_FAILED if is_gate else RunStatus.FAILED
            quality = exc.quality if isinstance(exc, SpectralQualityGateError) else None
            return self.repository.update_status(
                run.run_id,
                status,
                actual_range=resolved["actual_range"],
                result_summary={
                    "psd": None,
                    "band_power": None,
                    "relative_band_power": None,
                    "quality": quality,
                } if is_gate else None,
                error=StructuredRunError(
                    code="QUALITY_GATE_FAILED" if is_gate else "ANALYSIS_INPUT_INVALID",
                    message=str(exc),
                    stage="analysis",
                    details={"quality": quality} if quality is not None else {},
                ),
            )
        except Exception as exc:
            return self.repository.update_status(
                run.run_id,
                RunStatus.FAILED,
                actual_range=resolved["actual_range"],
                error=StructuredRunError(
                    code="ANALYSIS_EXECUTION_FAILED",
                    message=str(exc),
                    stage="analysis",
                    details={"exception_type": type(exc).__name__},
                ),
            )

    def get(self, run_id: str) -> AnalysisRun:
        run = self.repository.get(run_id)
        if run is None:
            raise KeyError("run not found")
        return run

    def list(self, recording_id: str | None = None, limit: int = 100) -> list[AnalysisRun]:
        return self.repository.list(recording_id, limit)

    def cancel(self, run_id: str) -> AnalysisRun:
        run = self.get(run_id)
        if run.status is not RunStatus.QUEUED:
            raise RunConflictError(f"run in {run.status.value} state cannot be cancelled")
        return self.repository.update_status(run_id, RunStatus.CANCELLED)

    def list_artifacts(self, run_id: str):
        run = self.get(run_id)
        source_id = run.reused_from_run_id or run.run_id
        return self.repository.list_artifacts(source_id)

    @staticmethod
    def _resolve_request(request: RunCreateRequest, recording: Any) -> dict[str, Any]:
        mapping = recording.mapping.__dict__ if recording.mapping else {}
        if request.analysis_type == "legacy_analysis":
            config = {"analysis_type": "legacy_analysis", **request.config}
            requested_range = {"start_s": 0.0, "end_s": float(recording.duration_s or 0.0)}
            actual_range = dict(requested_range)
            channels = [value for value in mapping.values() if value]
            definition = {"kind": "legacy_analysis", "contract": dict(ANALYSIS_CONTRACT)}
            scientific_version = ANALYSIS_ALGORITHM_VERSION
            window = {
                "welch_segment_s": ANALYSIS_CONTRACT["welch_segment_s"],
                "welch_overlap": ANALYSIS_CONTRACT["welch_segment_overlap"],
            }
        else:
            raw = dict(request.config)
            time = raw.get("time") or {}
            start = float(time.get("start_s", raw.get("start_s", 0.0)))
            default_window = 30.0
            end = float(time.get("end_s", start + float(raw.get("window_s", default_window))))
            mode = "spectrogram" if request.analysis_type == "spectrogram" else str(raw.get("mode", "static"))
            channels = list(raw.get("channels") or recording.channels)
            config_model = AnalysisConfigRequest.model_validate({
                "mode": mode,
                "channels": channels,
                "time": {"start_s": start, "end_s": end},
                "dynamic_window_s": raw.get("dynamic_window_s", 10),
                "refresh_step_s": raw.get("refresh_step_s", 1),
                "custom_frequency_range": raw.get("custom_frequency_range"),
            })
            config = config_model.model_dump(mode="json")
            requested_range = {"start_s": start, "end_s": end}
            sfreq = float(recording.sfreq or 1.0)
            duration = float(recording.duration_s or 0.0)
            actual_start = max(0.0, min(start, duration))
            start_index = int(np.floor(actual_start * sfreq))
            stop_index = min(int(round(duration * sfreq)), start_index + int(round((end - start) * sfreq)))
            actual_range = {"start_s": actual_start, "end_s": actual_start + max(0, stop_index - start_index) / sfreq}
            definition = {
                "kind": request.analysis_type,
                "contract": dict(ANALYSIS_CONTRACT),
                "spectrogram_contract": "spectrogram-v2" if request.analysis_type == "spectrogram" else None,
            }
            scientific_version = "spectrogram-v2" if request.analysis_type == "spectrogram" else str(ANALYSIS_CONTRACT["algorithm_version"])
            window = {
                "segment_s": 4.0,
                "step_s": 1.0 if request.analysis_type == "spectrogram" else 2.0,
                "overlap": 0.0 if request.analysis_type == "spectrogram" else 0.5,
                "alignment": "window_center" if request.analysis_type == "spectrogram" else "range",
            }
        return {
            "config": config,
            "requested_range": requested_range,
            "actual_range": actual_range,
            "channel_mapping": {"channels": channels, "semantic_mapping": mapping},
            "reference": {"mode": ANALYSIS_CONTRACT["reference"]},
            "filters": {key: ANALYSIS_CONTRACT[key] for key in (
                "bandpass_type", "bandpass_prototype_order", "bandpass_hz",
                "preprocessing_phase", "filter_form",
            )},
            "window": window,
            "quality_rules": {
                "minimum_clean_ratio": ANALYSIS_CONTRACT["minimum_clean_epoch_ratio"],
                "artifact_peak_uv": ANALYSIS_CONTRACT["artifact_peak_uv"],
                "reasons": ["non_finite", "amplitude_threshold", "flatline", "clipping", "missing_samples"],
            },
            "definition_sha256": sha256_json(definition),
            "scientific_version": scientific_version,
        }

    def _execute(self, analysis_type: str, recording: Any, resolved: dict[str, Any]):
        if analysis_type == "legacy_analysis":
            if recording.mapping is None:
                raise ValueError("saved semantic channel mapping is required")
            data, sfreq, channel_names, events = self.recordings.load_data(recording)
            result = analyze_recording(data, sfreq, recording.mapping, channel_names, events).to_dict()
            waveform = result.pop("waveform")
            metrics = result.pop("metrics")
            arrays: dict[str, np.ndarray] = {
                "waveform_elapsed_s": np.asarray(waveform["elapsed_s"], dtype=float),
            }
            for index, name in enumerate(waveform["channels"]):
                arrays[f"waveform_channel_{index}"] = np.asarray(waveform["channels"][name], dtype=float)
            arrays["metric_elapsed_s"] = np.asarray([item["elapsed_s"] for item in metrics], dtype=float)
            result.update({
                "metric_count": len(metrics),
                "waveform_channels": list(waveform["channels"]),
                "waveform_unit": waveform["unit"],
            })
            return result, arrays, "mixed; see result_summary"

        config = AnalysisConfigRequest.model_validate(resolved["config"])
        if analysis_type == "spectrum":
            payload = self.recordings.load_configured_spectrum(recording, config)
            frequencies = np.asarray(payload.pop("frequencies_hz"), dtype=float)
            psd = payload.pop("psd")
            arrays = {"frequency_hz": frequencies}
            for index, name in enumerate(payload["channels"]):
                arrays[f"psd_channel_{index}"] = np.asarray(psd[name], dtype=float)
            payload["artifact_channel_order"] = list(payload["channels"])
            return payload, arrays, "uV^2/Hz"

        payload = self.recordings.load_configured_spectrogram(recording, config)
        frequencies = np.asarray(payload.pop("frequencies_hz"), dtype=float)
        times = np.asarray(payload.pop("times_s"), dtype=float)
        linear = payload.pop("power_linear")
        payload.pop("power", None)
        db = payload.pop("power_db")
        arrays = {"frequency_hz": frequencies, "time_center_s": times}
        for index, name in enumerate(payload["channels"]):
            arrays[f"power_linear_channel_{index}"] = np.asarray(linear[name], dtype=float)
            arrays[f"power_db_channel_{index}"] = np.asarray(db[name], dtype=float)
        payload["artifact_channel_order"] = list(payload["channels"])
        return payload, arrays, "uV^2/Hz and dB re 1 uV^2/Hz"
