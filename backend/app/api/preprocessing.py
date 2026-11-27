"""Offline scientific preprocessing inspection endpoints (bad channels & artifact segments)."""

from __future__ import annotations

from typing import Optional

from fastapi import APIRouter, Query, Request

from app.core.api_contract import error_response
from app.models.preprocessing_preview import (
    PreprocessingPreviewWindowRequest,
    PreprocessingPreviewWindowResponse,
)
from app.models.artifact_references import ArtifactReferenceCatalogue, PreprocessingAnalysisChannels, ChannelIdentityUpdate
from app.models.analysis_config import OfflinePreprocessingRequest
from app.scientific.contracts.preprocessing import ArtifactRejectionSpec, ArtifactRemovalSpec, BadChannelSpec
from app.services.preprocessing.artifact_inspection import (
    inspect_recording_artifact_segments,
    inspect_recording_bad_channels,
    inspect_recording_ica_components,
)
from app.services.preprocessing.preprocessing_preview import build_preprocessing_preview_window
from app.services.preprocessing.preview_envelope import PreviewEnvelopeLimitError
from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
from app.services.recordings.recordings import RecordingService


router = APIRouter(prefix="/api/recordings", tags=["preprocessing"])


def _service(request: Request) -> RecordingService:
    return request.app.state.recording_service


def _offline_preprocessing(request: Request) -> OfflineScientificPreprocessingService:
    return request.app.state.offline_scientific_preprocessing_service


def _record_audit(request: Request, action: str, recording_id: str, parameters: dict[str, object]) -> None:
    request.app.state.audit_service.record(
        action, str(getattr(request.state, "request_id", "unknown")),
        recording_id=recording_id, parameters=parameters,
    )


@router.get("/{recording_id}/preprocessing/reference-channels", response_model=ArtifactReferenceCatalogue)
def reference_channels(recording_id: str, request: Request):
    try:
        from app.services.recordings.recording_channel_roles import discover_artifact_references
        service = _service(request)
        return discover_artifact_references(service, service.require_recording(recording_id))
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "ARTIFACT_REFERENCE_INVALID", str(exc))


@router.put("/{recording_id}/preprocessing/channel-identities", response_model=ArtifactReferenceCatalogue)
def update_channel_identities(recording_id: str, request: Request, body: ChannelIdentityUpdate):
    try:
        from app.services.recordings.recording_channel_roles import save_recording_identities
        service = _service(request)
        payload = save_recording_identities(service, service.require_recording(recording_id), body)
        _record_audit(request, "preprocessing.channel_identities", recording_id, {"channels": [row.model_dump() for row in body.channels]})
        return payload
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "CHANNEL_IDENTITY_INVALID", str(exc))


@router.post("/{recording_id}/preprocessing/analysis-channels", response_model=PreprocessingAnalysisChannels)
def analysis_channels(recording_id: str, request: Request, body: OfflinePreprocessingRequest,
                      montage_id: Optional[str] = Query(None)):
    try:
        from app.services.preprocessing.artifact_references import resolve_preprocessing_analysis_channels
        service = _service(request)
        return resolve_preprocessing_analysis_channels(service, service.require_recording(recording_id), body, montage_id)
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "PREPROCESSING_CHANNELS_INVALID", str(exc))


@router.get("/{recording_id}/preprocessing/bad-channels")
def inspect_bad_channels(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    duration_s: float = Query(60.0, gt=0.0, le=600.0),
) -> dict:
    """Screen raw unprojected EEG channels for bad-channel candidates and 3D interpolation eligibility."""
    try:
        service = _service(request)
        recording = service.require_recording(recording_id)
        payload = inspect_recording_bad_channels(service, recording, start_s=start_s, duration_s=duration_s)
        _record_audit(request, "preprocessing.inspect_bad_channels", recording_id, {
            "start_s": payload["start_s"],
            "end_s": payload["end_s"],
            "recommended_bad_channels": payload["recommended_bad_channels"],
        })
        return payload
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "BAD_CHANNEL_INSPECTION_INVALID", str(exc))


@router.get("/{recording_id}/preprocessing/artifact-segments")
def inspect_artifact_segments(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    duration_s: float = Query(60.0, gt=0.0, le=600.0),
    montage_id: Optional[str] = Query(None),
    peak_to_peak_uv: Optional[float] = Query(300.0, gt=0.0),
    muscle_zscore_threshold: Optional[float] = Query(4.0, gt=0.0),
    bad_channels: Optional[str] = Query(None),
    bad_channel_policy: str = Query("none"),
    low_hz: float = Query(1.0, gt=0.0),
    high_hz: float = Query(50.0, gt=0.0),
    notch_hz: Optional[float] = Query(50.0, ge=0.0),
    notch_q: float = Query(30.0, gt=0.0),
    eog_channels: Optional[str] = Query(None),
    ecg_channels: Optional[str] = Query(None),
) -> dict:
    """Return a bounded projection of the shared continuous artifact detection report."""
    try:
        service = _service(request)
        recording = service.require_recording(recording_id)
        parsed_bads = tuple(item.strip() for item in bad_channels.split(",") if item.strip()) if bad_channels else ()
        bad_spec = BadChannelSpec(policy=bad_channel_policy, channels=parsed_bads).normalized()  # type: ignore[arg-type]
        art_spec = ArtifactRejectionSpec(
            policy="annotate",
            peak_to_peak_uv=peak_to_peak_uv,
            muscle_zscore_threshold=muscle_zscore_threshold,
        ).normalized()
        payload = inspect_recording_artifact_segments(
            service,
            recording,
            start_s=start_s,
            duration_s=duration_s,
            montage_id=montage_id,
            bad_channel_spec=bad_spec,
            artifact_spec=art_spec,
            low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz or None, notch_q=notch_q,
            artifact_reference_channels=tuple(item.strip() for value in (eog_channels, ecg_channels)
                if value for item in value.split(",") if item.strip()),
            offline_preprocessing=_offline_preprocessing(request),
        )
        _record_audit(request, "preprocessing.inspect_artifact_segments", recording_id, {
            "start_s": payload["start_s"],
            "end_s": payload["end_s"],
            "montage_id": montage_id,
            "segment_count": len(payload["segments"]),
            "clean_ratio": payload["clean_ratio"],
            "inspection_filter": payload["inspection_filter"],
            "reference_channels": [item.strip() for value in (eog_channels, ecg_channels)
                if value for item in value.split(",") if item.strip()],
            "detection_scope": payload["detection_scope"],
        })
        return payload
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "ARTIFACT_SEGMENT_INSPECTION_INVALID", str(exc))


@router.get("/{recording_id}/preprocessing/ica-components")
def inspect_ica_components_endpoint(
    recording_id: str,
    request: Request,
    start_s: float = Query(0.0, ge=0.0),
    duration_s: Optional[float] = Query(None, gt=0.0, le=600.0),
    montage_id: Optional[str] = Query(None),
    low_hz: float = Query(0.5, gt=0.0),
    high_hz: float = Query(45.0, gt=0.0),
    notch_hz: Optional[float] = Query(50.0),
    bad_channels: Optional[str] = Query(None),
    bad_channel_policy: str = Query("none"),
    artifact_policy: str = Query("none"),
    peak_to_peak_uv: Optional[float] = Query(300.0, gt=0.0),
    muscle_zscore_threshold: Optional[float] = Query(4.0, gt=0.0),
    eog_channels: Optional[str] = Query(None),
    ecg_channels: Optional[str] = Query(None),
    allow_frontal_eog_proxy: bool = Query(False),
    ica_n_components: Optional[int] = Query(None, ge=2, le=64),
    ica_eog_threshold: float = Query(0.7, gt=0.0, le=1.0),
) -> dict:
    """Fit reproducible MNE infomax ICA and return reviewable component scores bound to a fit fingerprint."""
    try:
        service = _service(request)
        recording = service.require_recording(recording_id)
        parsed_bads = tuple(item.strip() for item in bad_channels.split(",") if item.strip()) if bad_channels else ()
        parsed_eog = tuple(item.strip() for item in eog_channels.split(",") if item.strip()) if eog_channels else ()
        bad_spec = BadChannelSpec(policy=bad_channel_policy, channels=parsed_bads).normalized()  # type: ignore[arg-type]
        art_spec = ArtifactRejectionSpec(
            policy=artifact_policy,  # type: ignore[arg-type]
            peak_to_peak_uv=peak_to_peak_uv,
            muscle_zscore_threshold=muscle_zscore_threshold,
        ).normalized()
        rem_spec = ArtifactRemovalSpec(
            method="ica_infomax",
            eog_channels=parsed_eog,
            ecg_channels=tuple(item.strip() for item in ecg_channels.split(",") if item.strip()) if ecg_channels else (),
            allow_frontal_eog_proxy=allow_frontal_eog_proxy,
            ica_n_components=ica_n_components,
            ica_eog_threshold=ica_eog_threshold,
        ).normalized()
        payload = inspect_recording_ica_components(
            service,
            recording,
            start_s=start_s,
            duration_s=duration_s,
            montage_id=montage_id,
            bad_channel_spec=bad_spec,
            artifact_rejection_spec=art_spec,
            removal_spec=rem_spec,
            low_hz=low_hz,
            high_hz=high_hz,
            notch_hz=notch_hz,
            offline_preprocessing=_offline_preprocessing(request),
        )
        _record_audit(request, "preprocessing.inspect_ica_components", recording_id, {
            "start_s": payload["start_s"],
            "end_s": payload["end_s"],
            "montage_id": montage_id,
            "n_components": payload["n_components"],
            "ica_fit_fingerprint": payload.get("ica_fit_fingerprint"),
            "recommended_exclude_components": payload["recommended_exclude_components"],
            "eog_channels_used": payload["eog_channels_used"],
            "ecg_channels_used": payload["ecg_channels_used"],
        })
        return payload
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "ICA_INSPECTION_INVALID", str(exc))


@router.post("/{recording_id}/preprocessing/preview-window", response_model=PreprocessingPreviewWindowResponse)
def preview_preprocessed_window(
    recording_id: str,
    request: Request,
    body: PreprocessingPreviewWindowRequest,
) -> dict:
    """Return gap-preserving display envelopes (uV), not resampled scientific signals."""
    try:
        service = _service(request)
        recording = service.require_recording(recording_id)
        bad_spec, art_spec, rem_spec = body.preprocessing.to_specs()
        payload = build_preprocessing_preview_window(
            service,
            recording,
            start_s=body.start_s,
            window_s=body.window_s,
            montage_id=body.montage_id,
            low_hz=body.low_hz,
            high_hz=body.high_hz,
            notch_hz=body.notch_hz,
            notch_q=body.notch_q,
            bad_channel_spec=bad_spec,
            artifact_rejection_spec=art_spec,
            artifact_removal_spec=rem_spec,
            max_points=body.max_points,
            offline_preprocessing=_offline_preprocessing(request),
        )
        return payload
    except PreviewEnvelopeLimitError as exc:
        return error_response(request, 422, "PREPROCESSING_PREVIEW_LIMIT_EXCEEDED", str(exc))
    except KeyError:
        return error_response(request, 404, "RECORDING_NOT_FOUND", "录制文件不存在")
    except ValueError as exc:
        return error_response(request, 422, "PREPROCESSING_PREVIEW_INVALID", str(exc))
