from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from starlette.exceptions import HTTPException as StarletteHTTPException

from app.api.recordings import router as recordings_router
from app.api.preprocessing import router as preprocessing_router
from app.api.recording_signal_preview import router as signal_preview_router
from app.api.recording_signal_export import router as signal_export_router
from app.services.viewer.recording_signal_export import RecordingSignalExporter
from app.api.electrode_locations import router as electrode_locations_router
from app.api.tnk_workbench import router as tnk_workbench_router
from app.api.playback import router as playback_router
from app.api.audit import router as audit_router
from app.api.events import router as events_router
from app.api.reports import router as reports_router
from app.api.runs import router as runs_router
from app.api.validations import router as validations_router
from app.services.workspace.audit import AuditService
from app.services.workspace.events import EventMarkerService
from app.services.workspace.reports import ReportSnapshotService
from app.services.recordings.recordings import RecordingService
from app.services.workspace.definitions import DefinitionService
from app.services.viewer.recording_waveform import RecordingWaveformService
from app.services.viewer.waveform_playback import WaveformPlaybackService
from app.services.analysis.runs.run_queue import PersistentRunQueue, RunWorker
from app.services.workspace.validations import ValidationService
from app.services.workspace.projects import ProjectService
from app.services.analysis.runs.batch_runs import BatchRunService
from app.api.projects import router as projects_router
from app.api.batch_runs import router as batch_runs_router
from app.api.results import router as results_router
from app.services.analysis.results.results import ResultService
from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
from app.services.analysis.spectral_analysis import SpectralAnalysisService
from app.services.analysis.runs.independent_spectral_reference import IndependentSpectralReferenceService
from app.services.workspace.extension_governance import ExtensionGovernanceService
from app.api.extensions import router as extensions_router
from app.api.algorithms import router as algorithms_router
from app.api.algorithm_presets import router as algorithm_presets_router
from app.api.live_filters import router as live_filters_router
from app.api.live_recordings import router as live_recordings_router
from app.api.analysis_windows import router as analysis_windows_router
from app.api.cache_maintenance import router as cache_maintenance_router
from app.api.topomap import router as topomap_router, dynamic_router as dynamic_topomap_router
from app.api.result_scalars import router as result_scalars_router
from app.api.custom_frequency_inspection import router as custom_frequency_inspection_router
from app.api.topomap_3d import router as topomap_3d_router
from app.services.viewer.live_filter import LiveFilterService
from app.services.live_analysis.live_recording_access import LiveRecordingAccessService
from app.bootstrap import build_builtin_registry
from app.services.live_analysis.live_analysis_contracts import LiveAnalysisAlgorithmRegistry
from app.services.live_analysis.live_analysis_coordinator import LiveAnalysisCoordinator
from app.services.live_analysis.live_rbp_algorithm import LiveRbpAlgorithm
from app.services.live_analysis.live_scientific_preprocessing import LiveScientificPreprocessingService
from app.services.live_analysis.live_spectral_algorithms import LiveSpectrumAlgorithm
from app.services.live_analysis.live_topomap_algorithm import LiveTopomapAlgorithm
from app.services.analysis.windows.on_demand_dynamic import DynamicWindowService
from app.services.analysis.windows.cache_maintenance import CacheMaintenanceService
from app.core.cache_maintenance_gate import CacheMaintenanceGate, CacheMaintenanceMiddleware
from app.services.analysis.scientific_compute_coordinator import ScientificComputeCoordinator
from app.services.analysis.results.topomap import TopomapService
from app.services.analysis.results.result_scalars import ResultScalarsService
from app.services.analysis.results.custom_frequency_inspection import CustomFrequencyInspectionService
from app.services.analysis.results.topomap_3d import Topomap3DService
from app.services.workspace.algorithm_presets import AlgorithmPresetService
from app.core.config import DATABASE_PATH, STORAGE_DIR, on_demand_algorithm_enabled
from app.core.api_contract import (
    RequestContextMiddleware,
    http_exception_handler,
    unhandled_exception_handler,
    validation_exception_handler,
)
from app.core.logging_setup import configure_logging
from app.services.analysis.results.current_algorithm_result import InvalidCurrentResultError, invalid_current_result_handler


configure_logging(STORAGE_DIR)


@asynccontextmanager
async def lifespan(application: FastAPI):
    application.state.run_worker.start()
    try:
        yield
    finally:
        application.state.run_worker.stop()
        application.state.dynamic_window_service.close()
        application.state.scientific_compute_coordinator.close()
        application.state.live_analysis_coordinator.shutdown()
        application.state.live_recording_access_service.close_all()


app = FastAPI(title="Brain Platform API", version="0.1.0", lifespan=lifespan)
app.add_middleware(CacheMaintenanceMiddleware, gate=CacheMaintenanceGate())
app.add_middleware(RequestContextMiddleware)
app.add_exception_handler(StarletteHTTPException, http_exception_handler)
app.add_exception_handler(RequestValidationError, validation_exception_handler)
app.add_exception_handler(InvalidCurrentResultError, invalid_current_result_handler)
app.add_exception_handler(Exception, unhandled_exception_handler)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173", "http://127.0.0.1:5173"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.state.recording_service = RecordingService()
app.state.offline_scientific_preprocessing_service = OfflineScientificPreprocessingService.for_recordings(
    app.state.recording_service
)
app.state.spectral_analysis_service = SpectralAnalysisService(
    app.state.recording_service,
    app.state.offline_scientific_preprocessing_service,
)
app.state.recording_waveform_service = RecordingWaveformService(
    app.state.recording_service.open_access_session
)
app.state.recording_service.add_delete_listener(
    app.state.recording_waveform_service.invalidate_recording
)
app.state.recording_signal_exporter = RecordingSignalExporter(
    STORAGE_DIR / "signal-exports", recording_storage=app.state.recording_service.storage_dir)
app.state.waveform_playback_service = WaveformPlaybackService(app.state.recording_service)
app.state.audit_service = AuditService()
app.state.event_marker_service = EventMarkerService()
app.state.report_snapshot_service = ReportSnapshotService()
app.state.scientific_compute_coordinator = ScientificComputeCoordinator()
app.state.algorithm_runtime_registry = build_builtin_registry()
app.state.run_service = PersistentRunQueue(
    app.state.recording_service,
    compute_coordinator=app.state.scientific_compute_coordinator,
    algorithm_registry=app.state.algorithm_runtime_registry,
    spectral_analysis=app.state.spectral_analysis_service,
)
app.state.run_worker = RunWorker(app.state.run_service)
app.state.validation_service = ValidationService()
app.state.definition_service = DefinitionService()
app.state.project_service = ProjectService()
app.state.batch_run_service = BatchRunService(app.state.project_service, app.state.run_service)
app.state.result_service = ResultService(app.state.run_service, app.state.validation_service)
app.state.independent_spectral_reference_service = IndependentSpectralReferenceService(
    app.state.recording_service,
    app.state.validation_service,
    spectral_analysis=app.state.spectral_analysis_service,
)
app.state.extension_governance_service = ExtensionGovernanceService()
app.state.algorithm_preset_service = AlgorithmPresetService(
    app.state.algorithm_runtime_registry,
    DATABASE_PATH,
)
app.state.live_filter_service = LiveFilterService()
app.state.live_recording_access_service = LiveRecordingAccessService()
app.state.live_scientific_preprocessing_service = LiveScientificPreprocessingService()
app.state.live_analysis_coordinator = LiveAnalysisCoordinator(
    LiveAnalysisAlgorithmRegistry((
        LiveRbpAlgorithm(preprocessor=app.state.live_scientific_preprocessing_service),
        LiveSpectrumAlgorithm("psd", preprocessor=app.state.live_scientific_preprocessing_service),
        LiveSpectrumAlgorithm("stft", preprocessor=app.state.live_scientific_preprocessing_service),
        LiveTopomapAlgorithm("rbp", preprocessor=app.state.live_scientific_preprocessing_service),
        LiveTopomapAlgorithm("psd", preprocessor=app.state.live_scientific_preprocessing_service),
    )),
    preprocessor=app.state.live_scientific_preprocessing_service,
)
app.state.dynamic_window_service = DynamicWindowService(
    app.state.recording_service,
    app.state.algorithm_runtime_registry,
    enabled_algorithms={
        module.manifest.algorithm_id
        for module in app.state.algorithm_runtime_registry.list()
        if on_demand_algorithm_enabled(
            module.manifest.algorithm_id,
            default_enabled=getattr(module, "on_demand_default_enabled", False),
        )
    },
    compute_coordinator=app.state.scientific_compute_coordinator,
    spectral_analysis=app.state.spectral_analysis_service,
)
app.state.topomap_service = TopomapService(
    app.state.result_service, app.state.run_service, app.state.dynamic_window_service,
)
app.state.cache_maintenance_service = CacheMaintenanceService(
    app.state.dynamic_window_service, app.state.run_service._offline_window_reuse.artifacts,
)
app.state.result_scalars_service = ResultScalarsService(app.state.dynamic_window_service)
app.state.custom_frequency_inspection_service = CustomFrequencyInspectionService(app.state.dynamic_window_service)
app.state.topomap_3d_service = Topomap3DService()
app.include_router(recordings_router)
app.include_router(preprocessing_router)
app.include_router(signal_preview_router)
app.include_router(signal_export_router)
app.include_router(electrode_locations_router)
app.include_router(tnk_workbench_router)
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
app.include_router(live_recordings_router)
app.include_router(analysis_windows_router)
app.include_router(cache_maintenance_router)
app.include_router(topomap_router)
app.include_router(dynamic_topomap_router)
app.include_router(result_scalars_router)
app.include_router(custom_frequency_inspection_router)
app.include_router(topomap_3d_router)


@app.get("/api/health")
def health() -> dict[str, str]:
    return {"status": "ok", "service": "brain-platform-backend"}
