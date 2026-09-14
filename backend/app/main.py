from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.recordings import router as recordings_router
from app.api.analyses import router as analyses_router
from app.api.playback import router as playback_router
from app.api.audit import router as audit_router
from app.api.events import router as events_router
from app.api.reports import router as reports_router
from app.api.runs import router as runs_router
from app.api.validations import router as validations_router
from app.api.algorithm_definitions import router as definition_router
from app.services.audit import AuditService
from app.services.events import EventMarkerService
from app.services.reports import ReportSnapshotService
from app.services.recordings import RecordingService
from app.services.playback import PlaybackService
from app.services.waveform_playback import WaveformPlaybackService
from app.services.runs import RunService
from app.services.validations import ValidationService
from app.services.definitions import DefinitionService
from app.core.api_contract import (
    RequestContextMiddleware,
    http_exception_handler,
    unhandled_exception_handler,
    validation_exception_handler,
)


app = FastAPI(title="Brain Platform API", version="0.1.0")
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
app.state.playback_service = PlaybackService(app.state.recording_service)
app.state.waveform_playback_service = WaveformPlaybackService(app.state.recording_service)
app.state.audit_service = AuditService()
app.state.event_marker_service = EventMarkerService()
app.state.report_snapshot_service = ReportSnapshotService()
app.state.run_service = RunService(app.state.recording_service)
app.state.validation_service = ValidationService()
app.state.definition_service = DefinitionService()
app.include_router(recordings_router)
app.include_router(analyses_router)
app.include_router(playback_router)
app.include_router(audit_router)
app.include_router(events_router)
app.include_router(reports_router)
app.include_router(runs_router)
app.include_router(validations_router)
app.include_router(definition_router)


@app.get("/api/health")
def health() -> dict[str, str]:
    return {"status": "ok", "service": "brain-platform-backend"}
