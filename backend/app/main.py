from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.recordings import router as recordings_router
from app.api.playback import router as playback_router
from app.api.audit import router as audit_router
from app.api.events import router as events_router
from app.api.reports import router as reports_router
from app.api.runs import router as runs_router
from app.api.validations import router as validations_router
from app.services.audit import AuditService
from app.services.events import EventMarkerService
from app.services.reports import ReportSnapshotService
from app.services.recordings import RecordingService
from app.services.definitions import DefinitionService
from app.services.waveform_playback import WaveformPlaybackService
from app.services.run_queue import PersistentRunQueue, RunWorker
from app.services.validations import ValidationService
from app.services.projects import ProjectService
from app.services.batch_runs import BatchRunService
from app.api.projects import router as projects_router
from app.api.batch_runs import router as batch_runs_router
from app.api.results import router as results_router
from app.services.results import ResultService
from app.services.independent_spectral_reference import IndependentSpectralReferenceService
from app.services.extension_governance import ExtensionGovernanceService
from app.api.extensions import router as extensions_router
from app.api.algorithms import router as algorithms_router
from app.api.algorithm_presets import router as algorithm_presets_router
from app.api.live_filters import router as live_filters_router
from app.services.live_filter import LiveFilterService
from app.bootstrap import build_builtin_registry
from app.services.algorithm_presets import AlgorithmPresetService
from app.core.config import DATABASE_PATH
from app.core.api_contract import (
    RequestContextMiddleware,
    http_exception_handler,
    unhandled_exception_handler,
    validation_exception_handler,
)


@asynccontextmanager
async def lifespan(application: FastAPI):
    application.state.run_worker.start()
    try:
        yield
    finally:
        application.state.run_worker.stop()


app = FastAPI(title="Brain Platform API", version="0.1.0", lifespan=lifespan)
app.add_middleware(RequestContextMiddleware)
app.add_exception_handler(StarletteHTTPException, http_exception_handler)
app.add_exception_handler(RequestValidationError, validation_exception_handler)
app.add_exception_handler(Exception, unhandled_exception_handler)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173", "http://127.0.0.1:5173"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.state.recording_service = RecordingService()
app.state.waveform_playback_service = WaveformPlaybackService(app.state.recording_service)
app.state.audit_service = AuditService()
app.state.event_marker_service = EventMarkerService()
app.state.report_snapshot_service = ReportSnapshotService()
app.state.run_service = PersistentRunQueue(app.state.recording_service)
app.state.run_worker = RunWorker(app.state.run_service)
app.state.validation_service = ValidationService()
app.state.definition_service = DefinitionService()
app.state.project_service = ProjectService()
app.state.batch_run_service = BatchRunService(app.state.project_service, app.state.run_service)
app.state.result_service = ResultService(app.state.run_service, app.state.validation_service)
app.state.independent_spectral_reference_service = IndependentSpectralReferenceService(
    app.state.recording_service, app.state.validation_service
)
app.state.extension_governance_service = ExtensionGovernanceService()
app.state.algorithm_runtime_registry = build_builtin_registry()
app.state.algorithm_preset_service = AlgorithmPresetService(
    app.state.algorithm_runtime_registry,
    DATABASE_PATH,
)
app.state.live_filter_service = LiveFilterService()
app.include_router(recordings_router)
app.include_router(playback_router)
app.include_router(audit_router)
app.include_router(events_router)
app.include_router(reports_router)
app.include_router(runs_router)
app.include_router(validations_router)
app.include_router(projects_router)
app.include_router(batch_runs_router)
app.include_router(results_router)
app.include_router(extensions_router)
app.include_router(algorithms_router)
app.include_router(algorithm_presets_router)
app.include_router(live_filters_router)


@app.get("/api/health")
def health() -> dict[str, str]:
    return {"status": "ok", "service": "brain-platform-backend"}
