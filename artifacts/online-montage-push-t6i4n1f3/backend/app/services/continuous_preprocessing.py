"""Shared identity-validated continuous EEG preparation; windows only slice its output."""

from dataclasses import replace
import logging
import time
from typing import Any

import numpy as np

from app.core.provenance import implementation_version
from app.scientific.contracts.analysis import ANALYSIS_CONTRACT
from app.algorithm_runtime.execution_control import ExecutionControl
from app.models.recording import RecordingSummary
from app.services.analysis_preprocess_cache import AnalysisPreprocessCache, PreprocessedRecording
from app.services.prepared_source_contract import PreparedSourceContract

logger = logging.getLogger("brain_platform.continuous_preprocessing")
POWERLINE_NOTCH_Q = float(ANALYSIS_CONTRACT["notch_quality_factor"])


class ContinuousPreprocessingService:
    """Own bounded reusable V/float64 sources across independent algorithm contexts."""

    def __init__(self, recordings: Any, preprocess_cache: AnalysisPreprocessCache):
        self.recordings = recordings
        self.preprocess_cache = preprocess_cache

    def load(self, recording: RecordingSummary, low_hz: float, high_hz: float,
             notch_hz: float | None, execution_context: ExecutionControl | None,
             channels: list[str] | None = None, *,
             requested_channels_only: bool = False) -> PreprocessedRecording:
        """Prepare continuously once per scientific identity, retaining existing resource limits."""
        from app.services.mapped_preprocessing import mapped_preprocess
        from app.services.prepared_source_identity import prepared_source_identity

        started = time.perf_counter()
        selected_channels = None
        if channels and (requested_channels_only or getattr(execution_context, "select_requested_channels", False)):
            # An explicit projection policy may opt into a smaller source.
            # Ordinary on-demand work first shares the canonical full source
            # with waveform and other algorithms when the resource budget fits.
            available = {name.casefold(): name for name in recording.channels}
            selected_channels = tuple(available[item.casefold()] for item in channels)
        elif channels:
            policy = getattr(execution_context, "preparation_resource_policy", None)
            if policy is not None:
                sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
                all_channel_bytes = sample_count * len(recording.channels) * np.dtype("float64").itemsize * 2
                if all_channel_bytes > policy.max_preparation_bytes:
                    available = {name.casefold(): name for name in recording.channels}
                    selected_channels = tuple(available[item.casefold()] for item in channels)
        identity = prepared_source_identity(self.recordings, recording, low_hz=low_hz,
            high_hz=high_hz, notch_hz=notch_hz, notch_q=POWERLINE_NOTCH_Q,
            channel_names=selected_channels)
        expected_contract = None
        if recording.sfreq is not None and recording.duration_s is not None and recording.channels:
            sample_count = int(round(float(recording.duration_s) * float(recording.sfreq)))
            contract_channels = selected_channels or tuple(recording.channels)
            unit_lookup = dict(zip(recording.channels, recording.channel_units or ("V",) * len(recording.channels)))
            expected_contract = PreparedSourceContract(
                schema_version="prepared-source-v1", identity=identity, status="ready",
                sample_rate_hz=float(recording.sfreq),
                prepared_start_sample=0, prepared_end_sample=sample_count,
                valid_start_sample=0, valid_end_sample=sample_count,
                channel_names=contract_channels,
                channel_units=tuple(unit_lookup[name] for name in contract_channels),
                preprocessing_manifest=identity, numerical_tolerance=1e-12,
                numerical_tolerance_evidence=(
                    "continuous FAA samples rtol=1e-11, atol=1e-18 V; "
                    "PSD full/on-demand rtol=1e-12, atol=1e-24 V^2/Hz"
                ),
                filter_phase="zero_phase",
                reference_semantics=str(
                    self.recordings.analysis_signal_provenance(recording).get(
                        "analysis_reference", "as_recorded"
                    )
                ),
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
            prepared = mapped_preprocess(
                self.recordings, recording, low_hz=low_hz, high_hz=high_hz,
                notch_hz=notch_hz, notch_q=POWERLINE_NOTCH_Q,
                channels=list(selected_channels) if selected_channels is not None else None,
                execution_context=execution_context,
            )
            if expected_contract is not None:
                prepared = replace(prepared, contract=expected_contract)
            return prepared
        cached = self.preprocess_cache.get_or_create(
            recording.id, identity, create_preprocessed, expected_contract=expected_contract,
        )
        completed = getattr(execution_context, "source_preparation_completed", None)
        if completed is not None:
            completed()
        logger.info(
            "continuous_source_perf recording_id=%s request_id=%s window_id=%s identity=%s "
            "lookup_hit=%s channels=%s sample_count=%s total_ms=%.3f",
            recording.id, getattr(execution_context, "request_id", None),
            getattr(execution_context, "window_id", None), identity, lookup.hit,
            list(cached.channel_names), len(cached.data), (time.perf_counter() - started) * 1000,
        )
        return cached
