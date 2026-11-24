"""Read scalar fields without spatial preparation or scientific re-execution."""

import math
from typing import Any

from app.models.result_scalars import ResultScalarField, ResultScalarPoint, ResultScalarsResponse
from app.services.result_scalar_inputs import RESULT_SCALAR_ADAPTERS, ResultProjectionUnavailable


class ResultScalarsService:
    def __init__(self, dynamic_window_service: Any):
        self.dynamic_window_service = dynamic_window_service

    def read_window(self, window_id: str, *, field_ids: list[str],
                    project_id: str | None = None) -> ResultScalarsResponse:
        payload = self.dynamic_window_service.get(window_id, project_id=project_id)
        identity = payload.get("identity") or {}
        algorithm = str(identity.get("algorithm_id", ""))
        adapter = RESULT_SCALAR_ADAPTERS.get(algorithm)
        if adapter is None:
            raise ResultProjectionUnavailable("SCALAR_ALGORITHM_UNSUPPORTED", "当前算法没有结果标量投影。")
        plan = payload.get("plan") or payload.get("range") or {}
        base = dict(window_id=window_id, recording_id=str(identity.get("recording_id", "")),
                    algorithm_id=algorithm, algorithm_version=str(identity.get("scientific_version", "unknown")),
                    implementation_version=str(identity.get("implementation_identity", "unknown")),
                    window_start_s=float(plan.get("start_s", 0)),
                    window_end_s=float(plan.get("end_s", 0)))
        if payload.get("state") != "ready" or not payload.get("result"):
            return ResultScalarsResponse(**base, status="unavailable", failure={
                "code": "SCALAR_WINDOW_UNAVAILABLE", "message": "当前窗口没有可用分析结果。",
            })
        source = adapter.build_input(recording_id=base["recording_id"], payload=payload,
            window_start_s=base["window_start_s"], window_end_s=base["window_end_s"], field_ids=field_ids)
        fields = []
        for field_id in field_ids:
            metadata = (source.metadata_by_field or {}).get(field_id, {})
            failure = source.unavailable or (source.failures_by_field or {}).get(field_id)
            values = source.values_by_band[field_id]
            points = []
            for index, channel in enumerate(source.channel_order):
                quality = (source.quality_by_channel or {}).get(channel, {})
                status = str(quality.get("status") or "unknown")
                reasons = [str(reason) for reason in quality.get("reasons", [])]
                raw = values[index] if index < len(values) and not failure else None
                valid = isinstance(raw, (int, float)) and not isinstance(raw, bool) and math.isfinite(raw) and raw >= 0
                if not valid:
                    reasons.append(str((failure or {}).get("code") or "SCALAR_VALUE_UNAVAILABLE"))
                points.append(ResultScalarPoint(channel=channel,
                    value=float(raw) if valid and status in {"clean", "warning"} else None,
                    quality_status=status, quality_reasons=reasons))
            fields.append(ResultScalarField(field_id=field_id, label=str(metadata.get("label", field_id)),
                unit=source.units_by_band[field_id], points=points,
                status="ready" if any(point.value is not None for point in points) else "unavailable",
                failure=failure, provenance={**(source.provenance or {}),
                    **(source.provenance_by_field or {}).get(field_id, {})}))
        return ResultScalarsResponse(**base, fields=fields,
            status="ready" if any(field.status == "ready" for field in fields) else "unavailable")
