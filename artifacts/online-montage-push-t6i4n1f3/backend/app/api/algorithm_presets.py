from __future__ import annotations

import sqlite3

from fastapi import APIRouter, Query, Request, status

from app.algorithm_runtime.errors import AlgorithmRuntimeError
from app.core.api_contract import error_response
from app.models.algorithm_preset import AlgorithmPresetCreateRequest, AlgorithmPresetUpdateRequest


router = APIRouter(prefix="/api/algorithm-presets", tags=["algorithm-presets"])


def _service(request: Request):
    return request.app.state.algorithm_preset_service


@router.get("")
def list_presets(request: Request, algorithm_id: str | None = Query(default=None)):
    return [item.model_dump(mode="json") for item in _service(request).list(algorithm_id)]


@router.post("", status_code=status.HTTP_201_CREATED)
def create_preset(payload: AlgorithmPresetCreateRequest, request: Request):
    try:
        return _service(request).create(payload).model_dump(mode="json")
    except sqlite3.IntegrityError:
        return error_response(request, 409, "ALGORITHM_PRESET_NAME_CONFLICT", "同一算法版本下预设名称已存在")
    except (AlgorithmRuntimeError, KeyError) as exc:
        return error_response(request, 422, "ALGORITHM_PRESET_INVALID", str(exc))
    except ValueError as exc:
        return error_response(request, 422, "ALGORITHM_PRESET_INVALID", str(exc))


@router.put("/{preset_id}")
def update_preset(preset_id: str, payload: AlgorithmPresetUpdateRequest, request: Request):
    try:
        return _service(request).update(preset_id, payload).model_dump(mode="json")
    except KeyError:
        return error_response(request, 404, "ALGORITHM_PRESET_NOT_FOUND", "算法参数预设不存在")
    except sqlite3.IntegrityError:
        return error_response(request, 409, "ALGORITHM_PRESET_NAME_CONFLICT", "同一算法版本下预设名称已存在")
    except (AlgorithmRuntimeError, ValueError) as exc:
        return error_response(request, 422, "ALGORITHM_PRESET_INVALID", str(exc))


@router.delete("/{preset_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_preset(preset_id: str, request: Request):
    try:
        _service(request).delete(preset_id)
    except KeyError:
        return error_response(request, 404, "ALGORITHM_PRESET_NOT_FOUND", "算法参数预设不存在")
