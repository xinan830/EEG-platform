"""TNK's frozen two-second finite-context preprocessing contract."""

from typing import Any

from app.services.bounded_window_preprocessing import PreparedWindow, prepare_window


TNK_WINDOW_PREPROCESSING_VERSION = "tnk-window-spectral-v1"
TNK_FILTER_CONTEXT_S = 2.0


def prepare_tnk_window(
    recordings: Any, recording: Any, *,
    start_sample: int, end_sample: int, channels: list[str] | None,
    low_hz: float, high_hz: float, notch_hz: float | None,
    execution_context: Any = None,
) -> PreparedWindow:
    """Read only TNK's target plus its existing two-second context."""
    with recordings.open_access_session(recording, verify_content_hash=False) as session:
        return prepare_window(
            session, start_sample=start_sample, end_sample=end_sample,
            channels=channels, low_hz=low_hz, high_hz=high_hz, notch_hz=notch_hz,
            context_s=TNK_FILTER_CONTEXT_S,
            algorithm_version=TNK_WINDOW_PREPROCESSING_VERSION,
            execution_context=execution_context,
        )
