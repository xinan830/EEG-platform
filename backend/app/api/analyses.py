from dataclasses import asdict

from fastapi import APIRouter, HTTPException, Request, status

from app.services.analysis import AnalysisService


router = APIRouter(tags=["analyses"])


def _service(request: Request) -> AnalysisService:
    return AnalysisService(request.app.state.recording_service)


@router.post("/api/recordings/{recording_id}/analysis", status_code=status.HTTP_201_CREATED)
def create_analysis(recording_id: str, request: Request) -> dict:
    try:
        return asdict(_service(request).create_analysis(recording_id))
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    except RuntimeError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.get("/api/analyses/{analysis_id}")
def get_analysis(analysis_id: str, request: Request) -> dict:
    try:
        return _service(request).get_result(analysis_id)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
