"""Shared identity-validated continuous EEG preparation; windows only slice its output."""

from dataclasses import dataclass, replace
from collections.abc import Callable
import logging
import time

import numpy as np

from app.core.provenance import implementation_version
from app.scientific.contracts.preprocessing import ArtifactRemovalSpec, OfflinePreprocessingSpec
from app.services.ica_review_cache import IcaReviewCache
from app.algorithm_runtime.execution_control import ExecutionControl
from app.models.recording import RecordingSummary
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.services.prepared_source_contract import PreparedSourceContract

logger = logging.getLogger("brain_platform.continuous_preprocessing")


@dataclass(frozen=True)
class PreprocessingRecordingAccess:
    open_access_session: Callable
    analysis_signal_provenance: Callable
    ica_review_cache: IcaReviewCache
    storage_dir: object = None


class ContinuousPreprocessingService:
    """Own bounded reusable V/float64 sources across independent algorithm contexts."""

    def __init__(self, recordings: PreprocessingRecordingAccess, preprocess_cache: AnalysisPreprocessCache):
        self.recordings = recordings
        self.preprocess_cache = preprocess_cache

    def invalidate_recording(self, recording_id: str) -> None:
        self.preprocess_cache.clear_recording(recording_id)
        self.recordings.ica_review_cache.invalidate_recording(recording_id)

    def load(self, recording: RecordingSummary, preprocessing_spec: OfflinePreprocessingSpec,
             execution_context: ExecutionControl | None = None,
             channels: list[str] | None = None, *,
             requested_channels_only: bool = False, _notify_prepared: bool = True,
             _artifact_reference_channels: tuple[str, ...] = ()) -> PreprocessedRecording:
        """Execute the facade's validated effective specification without reinterpreting it."""
        from app.services.mapped_preprocessing import mapped_preprocess
        from app.services.prepared_source_identity import prepared_source_identity

        started = time.perf_counter()
        from app.services.recording_channel_roles import attach_recording_identities, resolve_removal_references, identity_signature
        role_signature = identity_signature(self.recordings.storage_dir, recording)
        recording = attach_recording_identities(self.recordings.storage_dir, recording)
        if preprocessing_spec.artifact_removal.is_active:
            preprocessing_spec = replace(preprocessing_spec, artifact_removal=resolve_removal_references(
                self.recordings, recording, preprocessing_spec.artifact_removal))
        filters = preprocessing_spec.filter_spec
        low_hz, high_hz, notch_hz = filters.low_hz, filters.high_hz, filters.notch_hz
        norm_bad, norm_art, norm_rem = (preprocessing_spec.bad_channels,
                                       preprocessing_spec.artifact_rejection,
                                       preprocessing_spec.artifact_removal)
        bad_active, art_active, rem_active = norm_bad.is_active, norm_art.is_active, norm_rem.is_active
        from app.services.recording_channel_roles import dedicated_reference_sources, scientific_channel_names
        scientific_names = scientific_channel_names(self.recordings, recording)
        reference_channels = dedicated_reference_sources(self.recordings.storage_dir, recording,
            (*norm_rem.eog_channels, *norm_rem.ecg_channels))
        selected_channels = None
        if channels and (requested_channels_only or getattr(execution_context, "select_requested_channels", False)):
            # An explicit projection policy may opt into a smaller source.
            # Ordinary on-demand work first shares the canonical full source
            # with waveform and other algorithms when the resource budget fits.
            available = {name.casefold(): name for name in scientific_names}
            selected_channels = tuple(available[item.casefold()] for item in channels)
        elif channels:
            policy = getattr(execution_context, "preparation_resource_policy", None)
            if policy is not None:
                sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
                all_channel_bytes = sample_count * len(recording.channels) * np.dtype("float64").itemsize * 2
                if all_channel_bytes > policy.max_preparation_bytes:
                    available = {name.casefold(): name for name in scientific_names}
                    selected_channels = tuple(available[item.casefold()] for item in channels)
        # Artifact z-scores and interval boundaries belong to the complete channel population.
        if art_active or reference_channels or rem_active:
            selected_channels = None
        effective_notch_q = float(filters.notch_q)
        signal_provenance = self.recordings.analysis_signal_provenance(recording)
        identity = prepared_source_identity(self.recordings, recording, low_hz=low_hz,
            high_hz=high_hz, notch_hz=notch_hz, notch_q=effective_notch_q,
            channel_names=selected_channels, bad_channel_spec=norm_bad,
            preprocessing_spec=preprocessing_spec, artifact_reference_channels=reference_channels)
        expected_contract = None
        if recording.sfreq is not None and recording.duration_s is not None and recording.channels:
            sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
            contract_channels = selected_channels or scientific_names
            if reference_channels:
                from app.services.artifact_references import exclude_reference_outputs
                raw_labels = () if recording.extension == ".wpf" else recording.raw_channel_labels
                contract_channels = exclude_reference_outputs(contract_channels, raw_labels,
                    reference_channels, signal_provenance, norm_bad)
            elif bad_active and norm_bad.policy == "exclude" and selected_channels is None:
                from app.services.bad_channel_reader import predict_excluded_output_channels

                contract_channels = predict_excluded_output_channels(
                    contract_channels, norm_bad, signal_provenance,
                )
            expected_contract = PreparedSourceContract(
                schema_version="prepared-source-v1", identity=identity, status="ready",
                sample_rate_hz=float(recording.sfreq),
                prepared_start_sample=0, prepared_end_sample=sample_count,
                valid_start_sample=0, valid_end_sample=sample_count,
                channel_names=contract_channels,
                channel_units=("V",) * len(contract_channels),
                preprocessing_manifest=identity, numerical_tolerance=1e-12,
                numerical_tolerance_evidence=(
                    "continuous FAA samples rtol=1e-11, atol=1e-18 V; "
                    "PSD full/on-demand rtol=1e-12, atol=1e-24 V^2/Hz"
                ),
                filter_phase="zero_phase",
                reference_semantics=str(signal_provenance.get("analysis_reference", "as_recorded")),
                gap_policy="preserve",
                boundary_policy="valid_only",
                implementation_identity=implementation_version(),
            )
        lookup = self.preprocess_cache.lookup(recording.id, identity, expected_contract)
        if not lookup.hit:
            logger.info(
                "prepared_source_cache_miss recording_id=%s identity=%s reason=%s",
                recording.id, identity, lookup.miss_reason,
            )
        def create_preprocessed() -> PreprocessedRecording:
            annotated = None
            if art_active and (norm_art.policy != 'annotate' or rem_active):
                annotated = self.load(
                    recording, replace(preprocessing_spec,
                        artifact_rejection=replace(norm_art, policy='annotate'),
                        artifact_removal=ArtifactRemovalSpec()), execution_context,
                    _notify_prepared=False,
                    _artifact_reference_channels=reference_channels,
                )
            prepared = mapped_preprocess(
                self.recordings, recording, low_hz=low_hz, high_hz=high_hz,
                notch_hz=notch_hz, notch_q=effective_notch_q,
                channels=list(selected_channels) if selected_channels is not None else None,
                execution_context=execution_context,
                bad_channel_spec=norm_bad if bad_active else None,
                artifact_rejection_spec=norm_art if art_active else None,
                artifact_removal_spec=norm_rem if rem_active else None,
                annotated_source=annotated,
                artifact_reference_channels=(*norm_rem.eog_channels, *norm_rem.ecg_channels),
            )
            if expected_contract is not None:
                actual_contract = replace(
                    expected_contract,
                    channel_names=prepared.channel_names,
                    channel_units=("V",) * len(prepared.channel_names),
                )
                prepared = replace(prepared, contract=actual_contract)
            return prepared
        cached = self.preprocess_cache.get_or_create(
            recording.id, identity, create_preprocessed, expected_contract=expected_contract,
        )
        if identity_signature(self.recordings.storage_dir, recording) != role_signature:
            self.invalidate_recording(recording.id)
            raise ValueError("CHANNEL_IDENTITY_CHANGED: 准备期间通道身份已变化，请重新开始分析")
        completed = getattr(execution_context, "source_preparation_completed", None)
        if completed is not None and _notify_prepared:
            completed()
        logger.info(
            "continuous_source_perf recording_id=%s request_id=%s window_id=%s identity=%s "
            "lookup_hit=%s channels=%s sample_count=%s total_ms=%.3f",
            recording.id, getattr(execution_context, "request_id", None),
            getattr(execution_context, "window_id", None), identity, lookup.hit,
            list(cached.channel_names), len(cached.data), (time.perf_counter() - started) * 1000,
        )
        return cached
