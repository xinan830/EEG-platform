import pytest

from app.core.recording_time import cursor_sample_from_seconds, relative_sample_from_counter
from app.models.result_window import SampleRange, SavedResultWindow
from app.services.result_window_index import SavedWindowIndex


def _window(index: int, start: int, end: int, quality: str = "valid", reasons=()):
    return SavedResultWindow(
        window_id=f"window-{index}",
        window_index=index,
        start_sample=start,
        end_sample=end,
        window_state="Complete",
        quality_status=quality,
        reason_codes=reasons,
    )


def test_cursor_mapping_uses_floor_without_forward_epsilon():
    assert cursor_sample_from_seconds(3.999999, 1000, 48216) == 3999
    assert cursor_sample_from_seconds(4.0, 1000, 48216) == 4000
    assert cursor_sample_from_seconds(4.000001, 1000, 48216) == 4000


def test_relative_counter_preserves_nonzero_origin_and_rejects_reverse():
    assert relative_sample_from_counter(8_004_000, 8_000_000) == 4_000
    with pytest.raises(ValueError):
        relative_sample_from_counter(7_999_999, 8_000_000)


def test_latest_endpoint_is_selected_and_rejected_window_is_not_skipped():
    index = SavedWindowIndex(10_000, [_window(0, 0, 4_000), _window(1, 2_000, 6_000, "rejected", ("AMPLITUDE",))])

    assert index.select(3_999).status == "insufficient_data"
    selected = index.select(6_000)
    assert selected.status == "rejected"
    assert selected.window is not None and selected.window.window_index == 1


def test_gap_returns_no_stale_window_and_tail_is_explicit():
    index = SavedWindowIndex(
        10_000,
        [_window(0, 0, 4_000)],
        gaps=[SampleRange(start_sample=4_000, end_sample=5_000)],
    )

    gap = index.select(4_500)
    assert gap.status == "gap"
    assert gap.window is None
    assert index.select(9_000).has_uncovered_tail


def test_windows_must_have_strictly_increasing_unique_endpoints():
    with pytest.raises(ValueError):
        SavedWindowIndex(10_000, [_window(0, 0, 4_000), _window(1, 3_000, 4_000)])


def test_cursor_rejects_nonfinite_and_future_coordinates():
    with pytest.raises(ValueError):
        cursor_sample_from_seconds(float("nan"), 1000, 10000)
    with pytest.raises(ValueError):
        cursor_sample_from_seconds(10.001, 1000, 10000)
