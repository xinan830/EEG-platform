"""Read-only catalog of platform official algorithms."""

from fastapi import APIRouter, Request

from app.core.api_contract import error_response
from app.eeg_core.official_algorithms.registry import official_algorithm_catalog


router = APIRouter(prefix="/api/official-algorithms", tags=["official-algorithms"])


@router.get("")
def list_official_algorithms(request: Request):
    try:
        values = official_algorithm_catalog(request.app.state.definition_service)
    except RuntimeError:
        return error_response(
            request, 503, "OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE",
            "官方算法目录暂不可用，请检查平台定义安装状态",
        )
    return {"algorithms": [item.model_dump(mode="json") for item in values]}
