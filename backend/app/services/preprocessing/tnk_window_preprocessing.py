"""TNK's frozen two-second finite-context preprocessing contract."""

from typing import Any

from app.scientific.contracts.preprocessing import (
    ArtifactRejectionSpec,
    ArtifactRemovalSpec,
    BadChannelSpec,
    OfflinePreprocessingSpec,
    ScientificFilterSpec,
)
from app.services.preprocessing.bounded_window_preprocessing import PreparedWindow


TNK_WINDOW_PREPROCESSING_VERSION = "tnk-window-spectral-v1"
TNK_FILTER_CONTEXT_S = 2.0


def prepare_tnk_window(
    recordings: Any, recording: Any, *,
    start_sample: int, end_sample: int, channels: list[str] | None,
    low_hz: float, high_hz: float, notch_hz: float | None,
    execution_context: Any = None,
    bad_channel_spec: BadChannelSpec | None = None,
    artifact_rejection_spec: ArtifactRejectionSpec | None = None,
    artifact_removal_spec: ArtifactRemovalSpec | None = None,
    offline_preprocessing: Any = None,
) -> PreparedWindow:
    """Read only TNK's target plus its existing two-second context."""
    if offline_preprocessing is None:
        from app.services.preprocessing.offline_scientific_preprocessing import OfflineScientificPreprocessingService
        offline_preprocessing = OfflineScientificPreprocessingService.for_recordings(recordings)
    offline_prep = offline_preprocessing
    norm_bad = bad_channel_spec.normalized() if bad_channel_spec is not None else BadChannelSpec()
    norm_art = artifact_rejection_spec.normalized() if artifact_rejection_spec is not None else ArtifactRejectionSpec()
    norm_rem = artifact_removal_spec.normalized() if artifact_removal_spec is not None else ArtifactRemovalSpec()

    open_kwargs: dict[str, Any] = {"verify_content_hash": False}
    if norm_bad.is_active:
        open_kwargs["bad_channel_spec"] = norm_bad
    with recordings.open_access_session(recording, **open_kwargs) as session:
        full_spec = OfflinePreprocessingSpec(
            filter_spec=ScientificFilterSpec(
                low_hz=float(low_hz), high_hz=float(high_hz),
                notch_hz=None if notch_hz is None else float(notch_hz),
                filter_phase="zero_phase", scope="bounded_window_with_finite_context",
            ),
                bad_channels=norm_bad,
                artifact_rejection=norm_art,
                artifact_removal=norm_rem,
        )
        return offline_prep.prepare_bounded_window(
            session, start_sample=start_sample, end_sample=end_sample,
            channels=channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            context_s=TNK_FILTER_CONTEXT_S,
            algorithm_version=TNK_WINDOW_PREPROCESSING_VERSION,
            execution_context=execution_context,
            preprocessing_spec=full_spec,
            recording=recording,
        )
