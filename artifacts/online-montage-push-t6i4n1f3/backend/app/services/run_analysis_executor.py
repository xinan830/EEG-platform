"""Numerical execution for persisted analysis runs.

Run lifecycle, caching, persistence, and artifacts remain in ``RunService``.
This class deliberately owns only the pre-existing analysis dispatch paths.
"""

from __future__ import annotations

from typing import Any

import numpy as np
from pydantic import ValidationError

from app.services.algorithm_recording_context import _RecordingAlgorithmContext
from app.algorithm_runtime.contracts import (
    OFFICIAL_ALGORITHM_RESULT_CONTRACT_VERSION,
    AlgorithmResult,
    AlgorithmStructuredResult,
    AlgorithmStructuredSeriesResult,
)
from app.algorithm_runtime.executor import AlgorithmRuntime
from app.algorithm_runtime.errors import InvalidAlgorithmResultError
from app.algorithm_runtime.registry import AlgorithmRegistry
from app.models.analysis_config import AnalysisConfigRequest
from app.models.official_algorithm_run import OfficialAlgorithmRunConfig
from app.models.current_algorithm_result import CurrentAlgorithmResultSummary
from app.services.run_result_serialization import (
    _serialize_algorithm_result, _serialize_algorithm_series,
    _serialize_structured_result, _serialize_structured_series_result,
)


class RunAnalysisExecutor:
    def __init__(
        self,
        recordings: Any,
        algorithm_registry: AlgorithmRegistry | None = None,
    ):
        self.recordings = recordings
        self.algorithm_runtime = AlgorithmRuntime(algorithm_registry) if algorithm_registry is not None else None

    def execute(self, analysis_type: str, recording: Any, resolved: dict[str, Any]):
        if analysis_type == "official_algorithm":
            payload, arrays, unit = self._execute_official_algorithm(recording, resolved)
            payload["result_contract_version"] = OFFICIAL_ALGORITHM_RESULT_CONTRACT_VERSION
            payload["implementation_identity"] = resolved.get("implementation_identity") or self.algorithm_runtime.registry.get(
                OfficialAlgorithmRunConfig.model_validate(resolved["config"]).algorithm_id,
                resolved["scientific_version"],
            ).manifest.implementation_identity
            try:
                CurrentAlgorithmResultSummary.model_validate(payload)
            except ValidationError as exc:
                raise InvalidAlgorithmResultError("Serialized algorithm result violates its current contract") from exc
            return payload, arrays, unit

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

    def _execute_official_algorithm(self, recording: Any, resolved: dict[str, Any]):
        config = OfficialAlgorithmRunConfig.model_validate(resolved["config"])
        if self.algorithm_runtime is None:
            raise ValueError("algorithm runtime is not configured")
        context = _RecordingAlgorithmContext(self.recordings, recording, resolved.get("execution_context"))
        runtime_config = config.runtime_config()
        try:
            result = self.algorithm_runtime.execute(
                algorithm_id=config.algorithm_id,
                scientific_version=resolved["scientific_version"],
                recording=context,
                config=runtime_config,
                execution_context=resolved.get("execution_context"),
            )
        finally:
            context.close()
        module = self.algorithm_runtime.registry.get(config.algorithm_id, resolved["scientific_version"])
        label = module.manifest.display_name_zh
        if isinstance(result, AlgorithmStructuredSeriesResult):
            structured, arrays = _serialize_structured_series_result(result, config.algorithm_id, label)
            structured["requested_range"] = result.requested_range
            structured["actual_range"] = result.actual_range or result.requested_range
            return {"structured": structured}, arrays, None
        if isinstance(result, AlgorithmStructuredResult):
            structured, arrays = _serialize_structured_result(result, config.algorithm_id, label)
            return {"structured": structured}, arrays, None
        if isinstance(result, AlgorithmResult):
            point = _serialize_algorithm_result(result, config.algorithm_id, label)
            arrays = {"metric_value": np.asarray([np.nan if result.value is None else result.value], dtype=float)}
            if result.output_values is not None:
                arrays["band_share_values"] = np.asarray(list(result.output_values.values()), dtype=float)
            return {"metric": point}, arrays, result.unit
        return _serialize_algorithm_series(result, config, label)
