"""Recording API tests own their services and never start production workers."""

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from starlette.exceptions import HTTPException

from app.api.algorithms import router as algorithms_router
from app.api.analysis_windows import router as windows_router
from app.api.preprocessing import router as preprocessing_router
from app.api.projects import router as projects_router
from app.api.recordings import router as recordings_router
from app.api.runs import router as runs_router
from app.bootstrap import build_builtin_registry
from app.core.api_contract import (
    RequestContextMiddleware, http_exception_handler,
    unhandled_exception_handler, validation_exception_handler,
)
from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
from app.services.analysis.spectral_analysis import SpectralAnalysisService
from app.services.viewer.recording_waveform import RecordingWaveformService
from app.services.workspace.audit import AuditService
from app.services.workspace.definitions import DefinitionService
from app.services.workspace.projects import ProjectService


def recording_api_app(recordings) -> FastAPI:
    """Build only the recording/API dependencies used by the current test."""
    app = FastAPI(exception_handlers={
        HTTPException: http_exception_handler,
        RequestValidationError: validation_exception_handler,
        Exception: unhandled_exception_handler,
    })
    app.add_middleware(RequestContextMiddleware)
    app.state.recording_service = recordings
    app.state.offline_scientific_preprocessing_service = OfflineScientificPreprocessingService.for_recordings(recordings)
    app.state.spectral_analysis_service = SpectralAnalysisService(
        recordings, app.state.offline_scientific_preprocessing_service,
    )
    app.state.recording_waveform_service = RecordingWaveformService(recordings.open_access_session)
    recordings.add_delete_listener(app.state.recording_waveform_service.invalidate_recording)
    app.state.project_service = ProjectService(recordings.database_path)
    app.state.audit_service = AuditService(recordings.database_path)
    app.state.definition_service = DefinitionService(recordings.database_path)
    app.state.algorithm_runtime_registry = build_builtin_registry()
    for router in (recordings_router, preprocessing_router, projects_router,
                   algorithms_router, runs_router, windows_router):
        app.include_router(router)
    return app
